using System.Diagnostics;
using AIDataPlatform.Data;
using AIDataPlatform.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Azure.Security.KeyVault.Secrets;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace AIDataPlatform.Services;

public interface IDockerService
{
    Task<IList<ContainerListResponse>> GetContainersAsync();
    Task<ContainerInspectResponse> GetContainerDetailsAsync(string containerId);
    Task<bool> StartContainerAsync(string containerId);
    Task<bool> StopContainerAsync(string containerId);
    Task<bool> RemoveContainerAsync(string containerId);
    Task<string> CreateBrowserContainerAsync(string name = null);
    Task<int> GetContainerVncPortAsync(string containerId);
    
    // New session-based methods
    Task<string> CreateUserSessionAsync(string sessionId, string userId);
    Task<List<DataModel.SessionContainer>> GetUserSessionsAsync(string userId);
    Task<bool> StopUserSessionAsync(string sessionId, string userId);
    Task<DataModel.SessionContainer?> GetSessionAsync(string sessionId, string userId);
}

public class DockerService : IDockerService
{
    private readonly DockerClient _dockerClient;
    private readonly ILogger<DockerService> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IConfiguration _configuration;
    private const string BrowserImage = "ergenekon/magentic-python-vnc:latest";

    // SSH Configuration
    private readonly string _sshHost;
    private readonly string _sshUsername;
    private readonly string _sshPassword;
    private readonly string _baseDomain;

    public DockerService(ILogger<DockerService> logger, IDbContextFactory<ApplicationDbContext> contextFactory,
        IConfiguration configuration)
    {
        _dockerClient = new DockerClientConfiguration(
                new Uri("unix:///var/run/docker.sock"))
            .CreateClient();
        _logger = logger;
        _contextFactory = contextFactory;
        _configuration = configuration;

        // Load SSH configuration
        _sshHost = _configuration["RemoteDocker:SshHost"] ?? "172.161.106.65";
        _sshUsername = _configuration["RemoteDocker:SshUsername"] ?? "containeradmin";
        _sshPassword = _configuration["RemoteDocker:SshPassword"] ?? "";
        _baseDomain = _configuration["RemoteDocker:BaseDomain"] ?? "container.alpmind.ai";
    }

    // Existing methods remain the same
    public async Task<IList<ContainerListResponse>> GetContainersAsync()
    {
        try
        {
            return await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters { All = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting containers");
            throw;
        }
    }

    public async Task<ContainerInspectResponse> GetContainerDetailsAsync(string containerId)
    {
        try
        {
            return await _dockerClient.Containers.InspectContainerAsync(containerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting container details for {ContainerId}", containerId);
            throw;
        }
    }

    public async Task<bool> StartContainerAsync(string containerId)
    {
        try
        {
            await _dockerClient.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting container {ContainerId}", containerId);
            return false;
        }
    }

    public async Task<bool> StopContainerAsync(string containerId)
    {
        try
        {
            await _dockerClient.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping container {ContainerId}", containerId);
            return false;
        }
    }

    public async Task<bool> RemoveContainerAsync(string containerId)
    {
        try
        {
            await _dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing container {ContainerId}", containerId);
            return false;
        }
    }

    public async Task<string> CreateBrowserContainerAsync(string name = null)
    {
        try
        {
            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = BrowserImage
                },
                new AuthConfig(),
                new Progress<JSONMessage>());

            var parameters = new CreateContainerParameters
            {
                Image = BrowserImage,
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    { "6080/tcp", new EmptyStruct() }
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        {
                            "6080/tcp",
                            new List<PortBinding>
                            {
                                new()
                                {
                                    HostPort = "0"
                                }
                            }
                        }
                    },
                    ShmSize = 1073741824
                },
                Name = name ?? $"browser-{DateTime.Now:yyyyMMddHHmmss}"
            };

            var createResponse = await _dockerClient.Containers.CreateContainerAsync(parameters);

            await StartContainerAsync(createResponse.ID);
            await Task.Delay(5000);

            return createResponse.ID;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating browser container");
            throw;
        }
    }

    public async Task<int> GetContainerVncPortAsync(string containerId)
    {
        try
        {
            var container = await GetContainerDetailsAsync(containerId);

            if (container.NetworkSettings?.Ports == null ||
                !container.NetworkSettings.Ports.TryGetValue("6080/tcp", out IList<PortBinding>? value)) return 0;
            var binding = value?.FirstOrDefault();
            if (binding != null && int.TryParse(binding.HostPort, out int port))
            {
                return port;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting VNC port for container {ContainerId}", containerId);
            return 0;
        }
    }

    // New session-based methods
    public async Task<string> CreateUserSessionAsync(string sessionId, string userId)
    {
        try
        {
            // Generate unique container name and subdomain
            var containerName = $"session-{sessionId}-{userId}";
            var subdomain = GenerateSubdomain(sessionId, userId);
            var fullDomain = $"{subdomain}.{_baseDomain}";
            var containerUrl = $"https://{fullDomain}/vnc.html?resize=downscale&autoconnect=1";

            // Create container on remote server via SSH
            var dockerCommand = $"""
                                        docker run -d \
                                            --name {containerName} \
                                            --network web \
                                            --label traefik.enable=true \
                                            --label 'traefik.http.routers.{containerName}.rule=Host("{fullDomain}")' \
                                            --label traefik.http.routers.{containerName}.entrypoints=websecure \
                                            --label traefik.http.routers.{containerName}.service={containerName}-vnc \
                                            --label traefik.http.routers.{containerName}.tls.certresolver=letsencrypt \
                                            --label traefik.http.services.{containerName}-vnc.loadbalancer.server.port=6080 \
                                            --label 'traefik.http.routers.{containerName}-api.rule=Host("{fullDomain}") && PathPrefix(`/api`)' \
                                            --label traefik.http.routers.{containerName}-api.entrypoints=websecure \
                                            --label traefik.http.routers.{containerName}-api.service={containerName}-api \
                                            --label traefik.http.routers.{containerName}-api.tls.certresolver=letsencrypt \
                                            --label traefik.http.services.{containerName}-api.loadbalancer.server.port=8000 \
                                            --label session.id={sessionId} \
                                            --label user.id={userId} \
                                            {BrowserImage}
                                 """;

            var sshResult = await ExecuteSshCommandAsync(dockerCommand);

            if (string.IsNullOrEmpty(sshResult) || sshResult.Contains("Error"))
            {
                throw new Exception($"Failed to create remote container: {sshResult}");
            }

            // Save session to database with 'starting' status
            var sessionContainer = new DataModel.SessionContainer
            {
                SessionId = sessionId,
                UserId = userId,
                ContainerName = containerName,
                Url = containerUrl,
                Status = "starting",
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                IsActive = true
            };

            await using var context = await _contextFactory.CreateDbContextAsync();
            context.SessionContainers.Add(sessionContainer);
            await context.SaveChangesAsync();

            // Start background task to check container readiness
            await CheckContainerReadinessAsync(sessionId, userId, containerUrl);

            _logger.LogInformation("Created session container {ContainerName} for user {UserId}", containerName,
                userId);
            return sessionContainer.Url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user session for {SessionId}, {UserId}", sessionId, userId);
            throw;
        }
    }

    private async Task CheckContainerReadinessAsync(string sessionId, string userId, string containerUrl)
    {
        _logger.LogDebug("Checking container readiness for session {SessionId}", sessionId);

        var isReady = await WaitForHttpOkAsync(containerUrl);

        if (isReady)
        {
            await UpdateSessionStatusAsync(sessionId, userId, "running");
            _logger.LogInformation("Container is ready for session {SessionId}", sessionId);
        }
        else
        {
            await UpdateSessionStatusAsync(sessionId, userId, "failed");
            _logger.LogWarning("Container failed to become ready for session {SessionId}", sessionId);
        }
    }

    private async Task<bool> WaitForHttpOkAsync(string url, int timeoutMs = 20000, int intervalMs = 500)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                /* not ready */
            }

            await Task.Delay(intervalMs);
        }

        return false;
    }

    private async Task UpdateSessionStatusAsync(string sessionId, string userId, string status)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var session = await context.SessionContainers
                .FirstOrDefaultAsync(sc => sc.SessionId == sessionId && sc.UserId == userId);

            if (session != null)
            {
                session.Status = status;
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session status for {SessionId}", sessionId);
        }
    }


    public async Task<List<DataModel.SessionContainer>> GetUserSessionsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SessionContainers
            .Where(sc => sc.UserId == userId && sc.IsActive)
            .OrderByDescending(sc => sc.LastAccessedAt)
            .ToListAsync();
    }

    public async Task<bool> StopUserSessionAsync(string sessionId, string userId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var session = await context.SessionContainers
                .FirstOrDefaultAsync(sc => sc.SessionId == sessionId && sc.UserId == userId && sc.IsActive);

            if (session == null) return false;

            // Stop and remove container via SSH
            await ExecuteSshCommandAsync($"docker stop {session.ContainerName}");
            await ExecuteSshCommandAsync($"docker rm {session.ContainerName}");

            // Update database
            session.IsActive = false;
            session.Status = "stopped";
            await context.SaveChangesAsync();

            _logger.LogInformation("Stopped session container {ContainerName} for user {UserId}", session.ContainerName,
                userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping session {SessionId} for user {UserId}", sessionId, userId);
            return false;
        }
    }

    public async Task<DataModel.SessionContainer?> GetSessionAsync(string sessionId, string userId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var session = await context.SessionContainers
                .FirstOrDefaultAsync(sc => sc.SessionId == sessionId && sc.UserId == userId && sc.IsActive);

            if (session != null)
            {
                session.LastAccessedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                return session;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session {SessionId} for user {UserId}", sessionId, userId);
            return null; // Return null on error instead of throwing
        }
    }


// Helper methods
    private async Task<string> ExecuteSshCommandAsync(string command)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var client = CreateSshClient();
                client.Connect();
                
                using var cmd = client.CreateCommand(command);
                var result = cmd.Execute();
                var output = cmd.Result;
                var error = cmd.Error;
                
                client.Disconnect();

                if (string.IsNullOrEmpty(error)) return output;
                _logger.LogWarning("SSH command error: {Error}", error);
                return $"Error: {error}";

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH execution failed for command: {Command}", command);
                return $"Error: {ex.Message}";
            }
        });
    }

    private SshClient CreateSshClient()
    {
        var privateKeyContent = _configuration["privateKeySshTraefikBase64"];
        var keyBytes = Convert.FromBase64String(privateKeyContent);

        // Create in-memory stream from the private key content
        var keyStream = new MemoryStream(keyBytes);
        var privateKeyFile = new PrivateKeyFile(keyStream);

        var connectionInfo = new ConnectionInfo(_sshHost, _sshUsername,
            new PrivateKeyAuthenticationMethod(_sshUsername, privateKeyFile));
        
        return new SshClient(connectionInfo);
    }

    private static string GenerateSubdomain(string sessionId, string userId)
    {
        var hash = $"{sessionId}-{userId}".GetHashCode();
        var shortHash = Math.Abs(hash).ToString("x8");
        return $"session-{shortHash}";
    }
}