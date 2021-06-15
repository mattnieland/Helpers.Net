using System.IO;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class FormatWriter
	{
		public static void SaveFormatFile(string filename, SharpNodeMap nodeMap, Encoding encoding = null)
		{
			using (var stream = new FileStream(filename, FileMode.Create, FileAccess.Write))
			{
				using (var writer = new StreamWriter(stream, encoding ?? Encoding.GetEncoding(1252)))
				{

					foreach (var col in nodeMap.Columns)
					{
						var columnName = col.Node.Name;
						writer.WriteLine(string.Format("{0},{1},C", columnName, col.Width));
					}
					writer.WriteLine("EOR,2,B");
				}
			}
		}
	}
}
