using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class FlatReader : ISharpRowReader
	{
		private Stream _source;
		private Encoding _encoding;
		private SharpNodeMap _node;
		public int SkipLines = 0;
	    public bool SkipEmptyRows = false;

		public FlatReader(Stream source, SharpNodeMap node, Encoding encoding = null, bool skipEmptyRows = false)
		{
			_encoding = encoding ?? Encoding.UTF8;
			_source = source;
			_node = node;
            SkipEmptyRows = skipEmptyRows;
		}

		#region IEnumerable

		public IEnumerator<SharpRow> GetEnumerator()
		{
			using (var reader = new StreamReader(_source, _encoding))
			{
				for (int i = 0; i < SkipLines; i++)
				{
					if (reader.EndOfStream)
						break;
					reader.ReadLine();
				}

				while (!reader.EndOfStream)
				{
					var line = reader.ReadLine();
				    if (SkipEmptyRows && string.IsNullOrEmpty(line))
				        continue;

					yield return new SharpNodeRow(_node.Parent);
					yield return new SharpValueRow(_node, line);
				}
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion

		public void Dispose()
		{
			_source = null;

		}
	}

}
