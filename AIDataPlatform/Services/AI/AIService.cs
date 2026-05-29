using AIDataPlatform.Data;
using AIDataPlatform.Models;
using AIDataPlatform.Plugins;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Syncfusion.Blazor.InteractiveChat;
using AutoGen.Core;

namespace AIDataPlatform.Services.AI
{
    public class AIService
    {
        private readonly Kernel kernel;
        private IChatCompletionService? chatCompletionService;
        private readonly KernelMemoryService kernelMemoryService;
        private ChatHistory chatHistory;
        private readonly string systemPrompt;
        private readonly IDbContextFactory<ApplicationDbContext> dbContextFactory;
        private DataModel.UserChatHistory chat;
        private AutoGenMultiAgentSystem autoGenMultiAgentSystem;
        private PerplexityResearchAgent perplexityResearchAgent;
        private readonly string perplexityApiKey;

        public AIService(IConfiguration configuration, IDbContextFactory<ApplicationDbContext> dbContextFactory
            , AutoGenMultiAgentSystem autoGenMultiAgentSystem)
        {
            this.dbContextFactory = dbContextFactory;

            perplexityApiKey = configuration.GetSection("PerplexityAI")["ApiKey"];

            var openAISettings = configuration.GetSection("OpenAI");

            var builder = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(openAISettings.GetValue<string>("TextModel"), openAISettings.GetValue<string>("ApiKey"));

            // make configuration resolvable for plugins constructed by Semantic Kernel (e.g. EmailPlugin)
            builder.Services.AddSingleton<IConfiguration>(configuration);

            // can be configured independently if higher logging is needed for this specific service only
            //builder.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Information));

            // build semantic kernel
            kernel = builder.Build();
            
            // create kernel memory service
            kernelMemoryService = new KernelMemoryService(configuration);

            // add kernel memory service to semantic kernel as a memory plugin
            var memoryPlugin = new MemoryPlugin(kernelMemoryService.KernelMemory, waitForIngestionToComplete: true);
            kernel.ImportPluginFromObject(memoryPlugin, "memory");

            // add kernel email plugin
            kernel.ImportPluginFromType<EmailPlugin>();

            // add chatcompletion service
            chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            // chat setup
            systemPrompt = """
                           Answer questions briefly and concisely, get to the point immediately. Don't provide long explanations unless necessary.
                           Sometimes you don't have relevant memories so you reply saying you don't know, don't have the information.
                           Return the users asked info only on the below long term memory. He can also ask for other stuff in the memory.
                           """;

            // add chat history so that we can keep track of the conversation
            chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt); // add system prompt
            
            // Autogen MultiAgentSystem
            this.autoGenMultiAgentSystem = autoGenMultiAgentSystem;
        }

        
        public async Task<FunctionResult> SendPromptAsync(string prompt)
        {
            string chatPrompt = $"""
            <message role="user">{prompt}</message>
            <message role="system">Respond with short answers</message>
            """;

            var response = await kernel.InvokePromptAsync(prompt);

            return response;
        }

        public async Task<MemoryAnswer> SendSemanticPromptAsync(string prompt)
        {
            OpenAIPromptExecutionSettings settings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            var skPrompt = @"
            Question: {{$input}}

            Kernel Memory Answer: {{memory.ask $input}}
            ";

            KernelArguments arguments = new(settings)
            {
                { "input", prompt }
            };

            // response with kernel memory
            var response = await kernelMemoryService.KernelMemory.AskAsync(prompt);

            return response;
        }

        // primarily used method to use for semantic kernel chat completion
        public async Task<ChatMessageContent> SendSemanticPromptChatCompletionAsync(string prompt)
        {
            OpenAIPromptExecutionSettings settings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            var promptChatCompletion = $$"""
                                         
                                                        Question to Kernel Memory: {{prompt}}
                                         
                                                        Kernel Memory Answer: {memory.ask}
                                                        
                                         """;

            chatHistory.AddUserMessage(promptChatCompletion);

            var response = await chatCompletionService.GetChatMessageContentAsync(chatHistory, settings, kernel);
            
            chatHistory.AddAssistantMessage(response.ToString());

            return response;
        }

        public List<string> ExtractDocumentIdFromTag(MemoryAnswer semanticSearchResults)
        {
            List<string> documentIds = new();

            foreach (var result in semanticSearchResults.RelevantSources)
            {
                if (result.DocumentId != null)
                {
                    documentIds.Add(result.DocumentId);
                }
            }
            return documentIds;
        }
        
        public async Task<Guid> SaveChatHistoryAsync(string userId, string prompt, string response, Guid? chatHistoryId = null)
        {
            // Create fresh context for this separate operation
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            
            // If there's an existing chat history id, try to load that record.
            if (chatHistoryId.HasValue)
            {
                chat = await dbContext.ChatHistories
                    .FirstOrDefaultAsync(ch => ch.Id == chatHistoryId.Value && ch.UserId == userId);
            }

            if (chat == null)
            {
                // No existing chat found for the given id, so create a new one.
                chat = new DataModel.UserChatHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Prompts = new List<AssistViewPrompt>(),
                    SerializedChatHistory = new ChatHistory(),
                    LastModified = DateTime.UtcNow,
                    Created = DateTime.UtcNow
                };

                dbContext.ChatHistories.Add(chat);
            }
            
            if (chat.Prompts.Count > 0)
            {
                // Append the new prompt/response pair to the Prompts list
                chat.Prompts[^1] = new AssistViewPrompt
                {
                    Prompt = prompt,
                    Response = response
                };
            }
            else
            {
                chat.Prompts.Add(new AssistViewPrompt
                {
                    Prompt = prompt,
                    Response = response
                });
            }
            
            // Add the SK chat history to the UserChatHistory object 
            chat.SerializedChatHistory = chatHistory;
            
            // Update the timestamp to reflect the latest modification.
            chat.LastModified = DateTime.UtcNow;
            
            // EF Core will (de)serialize the collections for storage.
            await dbContext.SaveChangesAsync();

            // Return the id so your UI can keep track of the current chat session.
            return chat.Id;
        }

        /// <summary>
        /// Retrieves the chat history for a specific user
        /// </summary>
        public async Task<List<DataModel.UserChatHistory>> GetUserChatHistoryAsync(string userId)
        {
            // Create fresh context for this separate operation
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            
            var userChatHistory = await dbContext.ChatHistories
                .Where(ch => ch.UserId == userId)
                .OrderByDescending(ch => ch.Id)
                .ToListAsync();

            return userChatHistory;
        }
        
        /// <summary>
        /// Restores the semantic kernel chat history from a saved chat
        /// </summary>
        public async Task RestoreSemanticChatHistoryAsync(ChatHistory? savedChatHistory)
        {
            if (savedChatHistory == null)
            {
                // If no saved chat history, reset to just the system message
                chatHistory.Clear();
                chatHistory.AddSystemMessage(systemPrompt);
                return;
            }
            
            // Simply replace the current chat history with the saved one
            chatHistory = savedChatHistory;

            await Task.CompletedTask; // Since the operation is synchronous but we want an async interface
        }
        
        /// <summary>
       /// Sends a prompt to the AutoGen multi-agent system and returns the response
       /// </summary>
       /// <param name="prompt">The user prompt to process</param>
       /// <param name="cancellationToken">Optional cancellation token</param>
       /// <returns>The response from the multi-agent system</returns>
       public async Task<string> SendAutoGenPromptAsync(string prompt, CancellationToken cancellationToken = default)
       {
           try
           {
               // Check if autoGenMultiAgentSystem is initialized
               if (autoGenMultiAgentSystem == null)
               {
                   return "Error: The AutoGen multi-agent system is not initialized.";
               }

               // Process the prompt through the AutoGen multi-agent system
               var response = await autoGenMultiAgentSystem.ProcessPromptAsync(prompt, cancellationToken);

               // Save the conversation in chat history if needed
               // This is optional - remove if you don't want to save AutoGen conversations in the same history
               chatHistory.AddUserMessage(prompt);
               chatHistory.AddAssistantMessage(response);

               return response;
           }
           catch (Exception ex)
           {
               // Log the exception
               Console.WriteLine($"Error processing prompt with AutoGen: {ex.Message}");
               return $"I apologize, but I encountered an error processing your request with the multi-agent system: {ex.Message}";
           }
       }
        
        /// <summary>
        /// Sends a prompt directly to the Perplexity AI researcher for web search and research
        /// </summary>
        /// <param name="prompt">The user prompt to process</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The researched information from Perplexity AI</returns>
        public async Task<string> SendPerplexityResearchPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            perplexityResearchAgent = new(
                "researcher", 
                systemMessage:"you are a web researcher",
                apiKey: perplexityApiKey
                );
            
            try
            {
                // Create a user message with the prompt
                var userMessage = new TextMessage(Role.User, prompt, from: "user");
           
                // Send the message to the Perplexity agent
                var response = await perplexityResearchAgent.GenerateReplyAsync(
                    new List<IMessage> { userMessage },
                    cancellationToken);

                var responseContent = response.GetContent();

                return responseContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing research prompt with Perplexity: {ex.Message}");
                return $"I apologize, but I encountered an error while researching your query: {ex.Message}";
            }
        }
        
        public async Task<string> SendAutoGenSemanticKernelResearcherPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if autoGenMultiAgentSystem is initialized
                if (autoGenMultiAgentSystem == null)
                {
                    return "Error: The AutoGen multi-agent system is not initialized.";
                }

                // Process the prompt through the AutoGen multi-agent system
                var response = await autoGenMultiAgentSystem.ProcessResearcherPromptAsync(prompt, cancellationToken);

                // Save the conversation in chat history if needed
                // This is optional - remove if you don't want to save AutoGen conversations in the same history
                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(response);

                return response;
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error processing prompt with AutoGen: {ex.Message}");
                return $"I apologize, but I encountered an error processing your request with the multi-agent system: {ex.Message}";
            }
        }
        
        public async Task<string> SendAutoGenRoundRobinPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if autoGenMultiAgentSystem is initialized
                if (autoGenMultiAgentSystem == null)
                {
                    return "Error: The AutoGen multi-agent system is not initialized.";
                }

                // Process the prompt through the AutoGen multi-agent system
                var response = await autoGenMultiAgentSystem.ProcessRoundRobinPromptAsync(prompt, cancellationToken);

                // Save the conversation in chat history if needed
                // This is optional - remove if you don't want to save AutoGen conversations in the same history
                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(response);

                return response;
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error processing prompt with AutoGen: {ex.Message}");
                return $"I apologize, but I encountered an error processing your request with the multi-agent system: {ex.Message}";
            }
        }

        public async Task<string> SendRagPromptAsync(string prompt)
        {
            RagPlugin ragPlugin = new(kernelMemoryService);

            var response = await ragPlugin.SearchAsync(prompt);

            return response;
        }
    }
}

// old sendpromptasync function test method
/* 
 * public async Task<MemoryAnswer> SendSemanticPromptAsync(string prompt)
        {
            OpenAIPromptExecutionSettings settings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            var skPrompt = @"
            Question: {{$input}}

            Kernel Memory Answer: {{memory.ask $input}}
            ";

            KernelArguments arguments = new(settings)
        {
            { "input", prompt }//,
            //{ "history", chatHistory }
        };

            // add user prompt
            //chatHistory.AddUserMessage(prompt);

            // response with semantic kernel
            //var response = await kernel.InvokePromptAsync(skPrompt, arguments);

            // add bot message to chat history
            //chatHistory.AddAssistantMessage(response.ToString());


            // add web page
            //await kernelMemoryService.KernelMemory.ImportWebPageAsync("https://office.webando.ch/");


            //await kernelMemoryService.KernelMemory.ImportDocumentAsync(new Document("abc.pdf")
            //.);

            // response with kernel memory
            var response = await kernelMemoryService.KernelMemory.AskAsync(prompt);

            return response;
        }
        
        /// <summary>
   /// Sends a prompt directly to the Perplexity AI researcher for web search and research
   /// </summary>
   /// <param name="prompt">The user prompt to process</param>
   /// <param name="cancellationToken">Optional cancellation token</param>
   /// <returns>The researched information from Perplexity AI</returns>
   public async Task<string> SendPerplexityResearchPromptAsync(string prompt, CancellationToken cancellationToken = default)
   {
       try
       {
           // Create a user message with the prompt
           var userMessage = new TextMessage(Role.User, prompt, from: "user");
           
           // Send the message to the Perplexity agent
           var response = await perplexityResearchAgent.GenerateReplyAsync(
               new List<IMessage> { userMessage },
               cancellationToken);

           var responseContent = response.GetContent();

           return responseContent;
       }
       catch (Exception ex)
       {
           Console.WriteLine($"Error processing research prompt with Perplexity: {ex.Message}");
           return $"I apologize, but I encountered an error while researching your query: {ex.Message}";
       }
   }
 */