using System.Text;
using AIDataPlatform.Plugins;
using AIDataPlatform.Data;
using AIDataPlatform.Models;
using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AIDataPlatform.Services.AI
{
    /// <summary>
    /// A multi-agent system based on AutoGen .NET
    /// </summary>
    public class AutoGenMultiAgentSystem : IDisposable
    {
        private readonly Kernel kernel;
        private readonly KernelMemoryService kernelMemoryService;
        private readonly Dictionary<string, IAgent> _agents = new ();
        private readonly OpenAIClient _openAIClient;
        private readonly string _defaultModel;
        private readonly int _maxRounds;
        private readonly bool _verbose;
        private GroupChat _groupChat;
        private IAgent _orchestratorAgent;
        private IAgent _userProxyAgent;
        private string _perplexityApiKey;
        private MiddlewareStreamingAgent<SemanticKernelAgent> researcherAgent;
        private MiddlewareStreamingAgent<SemanticKernelAgent> memoryAgent;
        private IAgent summarizerAgent;
        private IAgent _groupAdmin;
        private IAgent _assistantAgent;
        private IEnumerable<ImageMessage>? chatHistory;
        private OpenAIPromptExecutionSettings? toolCallBehavior;

        /// <summary>
        /// Initializes a new instance of the AutoGenMemorySystem class
        /// </summary>
        /// <param name="kernelMemory">The KernelMemory instance</param>
        /// <param name="defaultModel">The default model to use</param>
        /// <param name="maxRounds">Maximum number of conversation rounds</param>
        /// <param name="verbose">Whether to output verbose logs</param>
        /// <param name="configuration"></param>
        public AutoGenMultiAgentSystem(IConfiguration configuration)
        {
            var openAISettings = configuration.GetSection("OpenAI");

            var builder = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(openAISettings. GetValue<string>("TextModel"), openAISettings.GetValue<string>("ApiKey"));

            // can be configured independently if higher logging is needed for this specific service only
            //builder.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Information));
            
            // create kernel memory service
            kernelMemoryService = new KernelMemoryService(configuration);

            // make configuration resolvable for plugins constructed by Semantic Kernel
            builder.Services.AddSingleton<IConfiguration>(configuration);

            builder.Plugins.AddFromType<EmailPlugin>();
            builder.Plugins.AddFromType<PerplexityResearchPlugin>();
            
            var memoryPlugin = new RagPlugin(kernelMemoryService);
            
            // add kernel memory service to semantic kernel as a memory plugin
            //var memoryPlugin = new MemoryPlugin(kernelMemoryService.KernelMemory, waitForIngestionToComplete: true);
            builder.Plugins.AddFromObject(memoryPlugin, "memory");
            
            // Tool call behavior
            toolCallBehavior = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };
            
            _defaultModel = configuration["OpenAI:TextModel"];
            _maxRounds = 10;

            // Initialize OpenAI client
            _openAIClient = new OpenAIClient(openAISettings.GetValue<string>("ApiKey"));
            
            // build semantic kernel
            kernel = builder.Build();

            // Initialize default agents
            InitializeDefaultAgents();
            
            // Configure the group chat
            ConfigureGroupChat();
        }

        /// <summary>
        /// Initializes the default agents
        /// </summary>
        private void InitializeDefaultAgents()
        {
            _groupAdmin = new OpenAIChatAgent(
                    chatClient: _openAIClient.GetChatClient(_defaultModel),
                    name: "groupAdmin",
                    systemMessage: """
                                   You are the admin of the group chat.
                                   """)
                .RegisterMessageConnector()
                .RegisterPrintMessage();
            
            // Create the user proxy agent
            _userProxyAgent = new UserProxyAgent(
                name: "userProxyAgent",
                humanInputMode: HumanInputMode.NEVER,
                defaultReply: "I've nothing to provide right now."
                )
                .RegisterPrintMessage();

            // Create the orchestrator agent (admin)
            _orchestratorAgent = new OpenAIChatAgent(
                name: "orchestrator",
                systemMessage: """
                               You are a manager who takes general questions, prompts from user and resolve them by splitting them into small tasks and assign each task to the most appropriate agent.
                               Here's available agents who you can assign task to:
                               - researcher: A specialized agent powered by GPT 4o with a perplexity ai plugin that can search the web for up-to-date information, find facts, and provide detailed research with sources
                               - userProxy: A proxy agent that only intervene when the users input is needed
                               - memory: A specialized agent that can retrieve information from a knowledge base
                               
                               You can use the following json format to assign task to agents:
                               ```task
                               {
                                   "to": "{agent_name}",
                                   "task": "{a short description of the task}",
                                   "context": "{previous context from scratchpad}"
                               }
                               ```
                               
                               If you need to ask user for extra information, you can use the following format:
                               ```ask
                               {
                                   "question": "{question}"
                               }
                               ```
                               
                               Once the users demand is resolved, summarize each steps that happened to the user using the following format:
                               '''summary
                               @user, <summary of the task>
                               '''
                               
                               Your reply must contain one of [task|ask|summary] to indicate the type of your message.
                               """,
                
                chatClient: _openAIClient.GetChatClient(_defaultModel))
                .RegisterMessageConnector()
                .RegisterPrintMessage();

            // Create the assistant agent
            _assistantAgent = new OpenAIChatAgent(
                name: "assistant",
                systemMessage: """
                               You are a helpful AI assistant. Your goal is to provide accurate, informative, and helpful responses to user queries.
                               Use your knowledge to answer questions directly when you can.
                               If you don't know something, admit it rather than making up information.
                               """,
                chatClient: _openAIClient.GetChatClient(_defaultModel))
                .RegisterMessageConnector()
                .RegisterPrintMessage();
            
            // Create the memory agent
            memoryAgent = kernel
                .ToSemanticKernelAgent(
                    name: "memory", 
                    systemMessage: """
                                   You are a specialized memory agent with access to a knowledge base.
                                   Your goal is to retrieve relevant information from the knowledge base to help answer user queries.
                                   Always provide accurate information based on what you find in the knowledge base.
                                   If you don't find relevant information, clearly state that the information is not available in the knowledge base."
                                   """,
                    settings: toolCallBehavior
                )
                .RegisterMessageConnector() // Register the message connector to support more AutoGen built-in message types
                .RegisterPrintMessage();
            
            researcherAgent = kernel
                .ToSemanticKernelAgent(
                    name: "researcher", 
                    systemMessage: $"""
                                   Your goal is to find accurate and up-to-date information from the web through perplexity AI function call, analyze it, and present it in a clear and organized way.
                                   Today's date is {DateTime.Now.ToString("yyyy-MM-dd")}.
                                   Only use the current date provided above when the user doesn't specify a particular date or any timeframe, day, whatsoever. If the user specifies any date in any form, ignore the datetime above.
                                   You always have to do a function call to do web searches with the perplexity ai plugin.
                                   Dont ask any further questions.
                                   If you cannot do the web search, return a message saying that you cannot do the web search.
                                   """,
                    settings: toolCallBehavior
                    )
                .RegisterMessageConnector() // Register the message connector to support more AutoGen built-in message types
                .RegisterPrintMessage();
            
            // Create the coder agent
            var coderAgent = new OpenAIChatAgent(
                name: "coder",
                systemMessage: """
                               You are a coding expert. Your goal is to write clean, efficient, and well-documented code.
                               Explain your code clearly, follow best practices, and help solve programming problems.
                               """,
                chatClient: _openAIClient.GetChatClient(_defaultModel))
                .RegisterMessageConnector()
                .RegisterPrintMessage();
            
            // Summarizer agent
            summarizerAgent = new OpenAIChatAgent(
                chatClient: _openAIClient.GetChatClient(_defaultModel),
                name: "summarizer",
                systemMessage: "You summarize search result in a short and concise manner")
                .RegisterMessageConnector()
                .RegisterPrintMessage();

            // Add agents to the dictionary
            _agents["userProxyAgent"] = _userProxyAgent;
            _agents["orchestrator"] = _orchestratorAgent;
            //_agents["assistant"] = _assistantAgent;
            _agents["memory"] = memoryAgent;
            _agents["researcher"] = researcherAgent;
            //_agents["coder"] = coderAgent;
            //_agents["summarizer"] = summarizerAgent;
        }
        

        /// <summary>
        /// Adds a custom agent to the system
        /// </summary>
        /// <param name="name">The agent name</param>
        /// <param name="agent">The agent to add</param>
        public void AddCustomAgent(string name, IAgent agent)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            if (agent == null)
                throw new ArgumentNullException(nameof(agent));

            _agents[name] = agent;

            if (_verbose)
                Console.WriteLine($"Added agent: {name}");
            
            // Reconfigure the group chat to include the new agent
            ConfigureGroupChat();
        }

        /// <summary>
        /// Removes an agent from the system
        /// </summary>
        /// <param name="name">The agent name</param>
        /// <returns>True if the agent was removed, false otherwise</returns>
        public bool RemoveAgent(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            var result = _agents.Remove(name);
            
            if (result && _verbose)
                Console.WriteLine($"Removed agent: {name}");
            
            // Reconfigure the group chat if an agent was removed
            if (result)
                ConfigureGroupChat();
                
            return result;
        }

        /// <summary>
        /// Gets all available agents
        /// </summary>
        /// <returns>A list of agent names</returns>
        public IEnumerable<string> GetAvailableAgents()
        {
            return _agents.Keys;
        }

        /// <summary>
        /// Configures the group chat with the default agents
        /// </summary>
        private void ConfigureGroupChat()
        {
            /*
            // Get all agents except the user proxy and admin
            var memberAgents = _agents.Values
                .ToList();
            */

            IEnumerable<IAgent> memberAgents =
            [
                _orchestratorAgent, researcherAgent, _userProxyAgent, memoryAgent //, summarizerAgent, _assistantAgent
            ];
            
            // Create the group chat with the admin agent and member agents
            _groupChat = new GroupChat(
                admin: _groupAdmin,
                members: memberAgents
                );

            //_groupChat = new RoundRobinGroupChat(memberAgents);
            
            // Send introductions for each agent to establish context
            //if (_agents.TryGetValue("assistant", out var assistant))
                //assistant.SendIntroduction("I will provide helpful and informative responses to general questions.", _groupChat);
                
            //if (_agents.TryGetValue("memory", out var memory))
                //memory.SendIntroduction("I will retrieve relevant information from the knowledge base to help answer queries.", _groupChat);
                
            if (_agents.TryGetValue("researcher", out var researcher))
                researcher.SendIntroduction("I will search the web, research topics, find information, and provide detailed analysis.", _groupChat);
                
            //if (_agents.TryGetValue("coder", out var coder))
                //coder.SendIntroduction("I will write, review, and explain code to solve programming problems.", _groupChat);
            
            //if (_agents.TryGetValue("summarizer", out var summarizer))
                //summarizer.SendIntroduction("I will summarize search results in a short and concise manner.", _groupChat);

            if (_agents.TryGetValue("orchestrator", out var orchestrator))
                orchestrator.SendIntroduction("I am your manager. I will take care of all your requests.", _groupChat);
            
            if (_agents.TryGetValue("userProxyAgent", out var user))
                user.SendIntroduction("I am the user proxy agent. I will only intervene when the users input is needed", _groupChat);
            
            if (_agents.TryGetValue("memory", out var memory))
                memory.SendIntroduction("I am the memory agent. I will retrieve relevant information from the knowledge base to help answer queries.", _groupChat);
        }

        /// <summary>
        /// Processes a user prompt through the multi-agent system
        /// </summary>
        /// <param name="userPrompt">The user prompt</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The final response</returns>
        public async Task<string> ProcessPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userPrompt))
                throw new ArgumentNullException(nameof(userPrompt));

            try
            {
                // Create a message for the user prompt
                var message = new TextMessage(Role.User, userPrompt, from: "user");
                
                // Process the message through the group chat
                var conversationBuilder = new StringBuilder();
                
                await foreach (var responseMessage in _groupChat.SendAsync([message], maxRound: _maxRounds, cancellationToken: cancellationToken))
                {
                    conversationBuilder.AppendLine($"**{responseMessage.From}**: {responseMessage.GetContent()}");
                    conversationBuilder.AppendLine(); // Add an extra line between messages
                    
                    if (responseMessage.From == _orchestratorAgent.Name && responseMessage.GetContent().Contains("```summary") | responseMessage.GetContent().Contains("@user"))
                    {
                        // Task complete!
                        break;
                    }
                }
                
                return conversationBuilder.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"I apologize, but I encountered an error processing your request: {ex.Message}";
            }
        }

        public async Task<string> ProcessRoundRobinPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
        {
            var groupChatAgent = new GroupChatManager(_groupChat);
            var reply = await _userProxyAgent.InitiateChatAsync(
                receiver: groupChatAgent,
                message: userPrompt,
                maxRound: 1
            );

            return reply.LastOrDefault()?.GetContent();
        }

        public async Task<string> ProcessResearcherPromptAsync(string userPrompt,
            CancellationToken cancellationToken = default)
        {
            var prompt =  MessageEnvelope.Create(new Microsoft.SemanticKernel.ChatMessageContent(AuthorRole.User, userPrompt));
            var reply = await researcherAgent.SendAsync(prompt);
            
            return reply.GetContent();
        }

        /// <summary>
        /// Disposes the resources used by the AutoGenMemorySystem
        /// </summary>
        public void Dispose()
        {
            foreach (var agent in _agents.Values)
            {
                (agent as IDisposable)?.Dispose();
            }
        }
    }
}


/*
var memberAgents = _agents.Values
   .Where(a => a.Name != "user" && a.Name != "orchestrator")
   .ToList();
   */
   
   
/*
public async Task<string> ProcessPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
   {
       if (string.IsNullOrEmpty(userPrompt))
           throw new ArgumentNullException(nameof(userPrompt));

       try
       {
           // Create a message for the user prompt
           var message = new TextMessage(Role.User, userPrompt);
           
           // Create a result collector
           List<IMessage> resultCollector = new ();
           var lastOrchestratorMessage = string.Empty;
           
           // Process the message through the group chat
           await foreach (var responseMessage in _groupChat.SendAsync(
               [message],
               maxRound: _maxRounds,
               cancellationToken: cancellationToken))
           {
               
               resultCollector.Add(responseMessage);
           }
           
           // Return the last message from the orchestrator as the final response
           return string.IsNullOrEmpty(lastOrchestratorMessage) 
               ? "No response was generated by the orchestrator." 
               : lastOrchestratorMessage;
       }
       catch (Exception ex)
       {
           return $"I apologize, but I encountered an error processing your request: {ex.Message}";
       }
   }
   
   You always have to do a function call to do web searches with the perplexity ai plugin.
Dont ask any further questions, just do the web search with that plugin. And return the results formatted to the user.
If you cannot do the web search, return a message saying that you cannot do the web search.
*/

// Create the researcher agent using Perplexity AI
            /*
            var researcherAgent = new OpenAIChatAgent(
                    name: "researcher",
                    systemMessage: """
                                   You are a research specialist that leverages Perplexity AI search capabilities via function call. Your goal is to find accurate and up-to-date information from the web, analyze it, and present it in a clear and organized way.
                                   Break down complex topics, cite sources when possible, and provide comprehensive analysis.
                                   Always search for the most current information available and provide references to your sources.
                                   When researching coding topics, focus on best practices, documentation, and real-world examples.
                                   """,
                    chatClient: _openAIClient.GetChatClient(_defaultModel))

                apiKey: _perplexityApiKey,
                model: "sonar-medium-online", // You can choose between sonar-small-online, sonar-medium-online, or sonar-large-online
                verbose: _verbose);
                .RegisterMessageConnector()
                .RegisterPrintMessage();
                                */
            
            /*
            researcherAgent = new SemanticKernelAgent(
                kernel: kernel,
                name: "researcher",
                systemMessage: """
                               You are a research specialist. Your goal is to find accurate and up-to-date information from the web through perplexity AI function call, analyze it, and present it in a clear and organized way.
                               Break down complex topics, cite sources when possible, and provide comprehensive analysis.
                               Always search for the most current information available and provide references to your sources.
                               When researching coding topics, focus on best practices, documentation, and real-world examples.
                               """,
                settings: new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                }
            )
            .RegisterMessageConnector()
            .RegisterPrintMessage(); // pretty print the message;
            */
            