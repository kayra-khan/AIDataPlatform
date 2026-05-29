using Azure.Storage.Blobs.Models;
using Microsoft.SemanticKernel.ChatCompletion;
using Syncfusion.Blazor.InteractiveChat;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AIDataPlatform.Data;

namespace AIDataPlatform.Models
{
    public static class DataModel
    {
        // Tenant data model
        public class Tenant
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public List<ApplicationUser>? Users { get; set; }
            /*
            public string? Email { get; set; }
            public string? SubscriptionPlan { get; set; }
            public DateTime? SubscriptionStartDate { get; set; }
            public DateTime? SubscriptionEndDate { get; set; }
            */
        }

        // Blob storage data model
        public class BlobInfo
        {
            public string? Name { get; set; }
            public Uri? Uri { get; set; }
            public BlobItemProperties? Property { get; set; }
            public string? ContainerName { get; set; }

            // Latest version of blob. if blob is removed, in versioned blob storage, the latest version will be null
            public bool? IsLatestVersion { get; set; }
        }

        // Azure comsosdb data model
        public class Document
        {
            // id needs to be lower case because of cosmosdb
            public string? id { get; set; }
            public string? TenantId { get; set; }
            public string? DocumentTypeId { get; set; }
            public string? DateId { get; set; }
            public string? DocumentId { get; set; }
            public bool? IsDeleted { get; set; }
            public string? OriginalContent { get; set; }
            public string? KeyValuePairs { get; set; }
        }        

        // AI search data model class with kernel memorys default index name 'default'
        public class AISearchDefaultDocument
        {
            public string? Id { get; set; }
            public string[]? Tags { get; set; }
            public string? Payload { get; set; }
        }

        // To be able to deserialize originalcontent from search service property in order to access sub properties inside search.razor page
        public class OriginalContent
        {
            public string? Content { get; set; }
        }

        public class Element
        {
            public Key? Key { get; set; }
            public Value? Value { get; set; }
            public double Confidence { get; set; }
        }

        public class Key
        {
            public string? Content { get; set; }
            public List<BoundingRegion>? BoundingRegions { get; set; }
        }

        public class Value
        {
            public string? Content { get; set; }
            public List<BoundingRegion>? BoundingRegions { get; set; }
        }

        public class BoundingRegion
        {
            public int PageNumber { get; set; }
            public List<BoundingPolygon> BoundingPolygon { get; set; }
        }

        public class BoundingPolygon
        {
            public bool IsEmpty { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }

        public class Invitation
        {
            public int Id { get; set; }
            public string Email { get; set; }
            public string TenantId { get; set; }
            public string Token { get; set; }
            public DateTime Expiration { get; set; }
            public bool IsAccepted { get; set; }
        }

        public class PasswordModel
        {
            public string Password { get; set; }
            public string ConfirmPassword { get; set; }
        }
        
        // UserChatHistory.cs
        public class UserChatHistory
        {
            public Guid Id { get; set; }
            public string UserId { get; set; }
            public List<AssistViewPrompt> Prompts { get; set; }
            public ChatHistory SerializedChatHistory { get; set; }
            public DateTime LastModified { get; set; }
            public DateTime Created { get; set; }
        }

        public class SessionContainer
        {
            [Key]
            public Guid Id { get; set; } = Guid.NewGuid();
    
            [Required]
            public string SessionId { get; set; } = string.Empty;
    
            [Required]
            public string UserId { get; set; } = string.Empty;
    
            [Required]
            public string ContainerName { get; set; } = string.Empty;
    
            [Required]
            public string Url { get; set; } = string.Empty;
    
            public string Status { get; set; } = "unknown";
    
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
            public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    
            public bool IsActive { get; set; } = true;
    
            // Navigation property to ApplicationUser
            [ForeignKey(nameof(UserId))]
            public ApplicationUser User { get; set; } = null!;
        }
    }
}

/* old chatbot data model
 // chatbot.razor shared component data model
        public class ChatModel
        {
            public string Id { get; set; }
            public string Chat { get; set; }
            public string Pic { get; set; }
            public string Avatar { get; set; }
            public string MessageTitle { get; set; }
            public string MessageValue { get; set; }
        }
*/

/*
  // AI search custom data model - not used anymore
        public class AiSearchDocument
        {
            public string? id { get; set; }
            public string? TenantId { get; set; }
            public string? DocumentTypeId { get; set; }
            public string? DateId { get; set; }
            public string? DocumentId { get; set; }
            public string? OriginalContent { get; set; }
            public IEnumerable<dynamic>? KeyValuePairs { get; set; }
            public string? Language { get; set; }
            public string? KeyPhrases { get; set; }
            public string? TranslatedText { get; set; }
        }
*/