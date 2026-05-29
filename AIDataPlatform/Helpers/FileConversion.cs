using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;

namespace AIDataPlatform.Helpers
{
	public static class FileConversion
	{
		public static async Task<MemoryStream> ConvertWordToPdfAsync(Stream blobStream)
		{
			return await Task.Run(() =>
			{
				using WordDocument wordDocument = new(blobStream, FormatType.Automatic);

				// Instantiation of DocIORenderer for Word to PDF conversion
				using DocIORenderer render = new();

				// Sets Chart rendering Options.
				render.Settings.ChartRenderingOptions.ImageFormat = Syncfusion.OfficeChart.ExportImageFormat.Jpeg;

				// Converts Word document into PDF document
				PdfDocument pdfDocument = render.ConvertToPDF(wordDocument);

				// Saves the PDF file
				MemoryStream outputStream = new();

				pdfDocument.Save(outputStream);

				// Closes the instance of PDF document object
				pdfDocument.Close();

				// Reset the position of the MemoryStream to the beginning
				outputStream.Position = 0;

				return outputStream;
			});
		}
	}
}
