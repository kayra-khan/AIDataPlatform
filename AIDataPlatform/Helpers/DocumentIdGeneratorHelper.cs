using System;

namespace AIDataPlatform.Helpers
{
    public static class DocumentIdGeneratorHelper
    {
        public static string GenerateDocumentId()
        {
            return DateTime.UtcNow.ToString("yyMMdd") + Guid.NewGuid().ToString("N");
        }
    }
}