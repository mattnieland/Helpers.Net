using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
	public class FlatFile : ISharpObjectStream
	{
		private StreamReader _reader = StreamReader.Null;
		private StreamWriter _writer = StreamWriter.Null;

		protected Stream Buffer = null;
		protected Encoding Encoding = Encoding.ASCII;
		protected bool HeaderComplete = false;

		public FlatFileRow RowFormat = new FlatFileRow();
		public string FileName = null;
		
		protected virtual string ReadLine()
		{
			return _reader.ReadLine();
		}

		protected virtual bool EndOfStream
		{
			get { return _reader.EndOfStream; }
		}

		protected virtual void WriteLine(string line)
		{
			_writer.WriteLine(line);
		}

		public virtual IEnumerable<SharpObject> Select()
		{
			
			while (Buffer != null)
			{
				if (EndOfStream)
				{
					Close();
					continue;
				}

				var line = ReadLine();
				yield return RowFormat.GetObject(line);
			}
		}

	    public virtual IEnumerable<Dictionary<string, string>> SelectFlat()
	    {
            while (Buffer != null)
            {
                if (EndOfStream)
                {
                    Close();
                    continue;
                }

                var line = ReadLine();
                yield return RowFormat.GetFlatObject(line);
            }
        }
		
		public virtual void Write(SharpObject source)
		{
			if (Buffer != null)
			{
				var line = RowFormat.GetRowData(source);
				WriteLine(line);
			}
		}

		public virtual void Write(IEnumerable<SharpObject> source)
		{
			if (Buffer != null)
			{
				foreach (var obj in source)
				{
					var line = RowFormat.GetRowData(obj);
					WriteLine(line);
				}
			}
		}

		public void Dispose()
		{
			Close();
		}

		public virtual void Close()
		{
			if (_reader != StreamReader.Null)
			{
				_reader.Close();
				_reader = StreamReader.Null;
			}

			if (_writer != StreamWriter.Null)
			{
				if (_writer != null)
				{
					_writer.Flush();
					_writer.Close();
					_writer = StreamWriter.Null;
				}
			}

			Buffer = null;
		}

		public void LoadFmtFile(string fmtFile = null)
		{
			if (string.IsNullOrEmpty(fmtFile) && !string.IsNullOrEmpty(FileName))
				fmtFile = Path.Combine(Path.GetDirectoryName(FileName)??"", Path.GetFileNameWithoutExtension(FileName) + ".fmt");

			if (!string.IsNullOrEmpty(fmtFile) && File.Exists(fmtFile))
				RowFormat.LoadFormatFile(fmtFile);
		}

		public static int AutoDetectVersion(string filename)
		{
			using (var reader = new StreamReader(filename))
			{
				if (reader.EndOfStream) return 0;

				var line = reader.ReadLine();

				if (string.IsNullOrEmpty(line) || line.Length < 3) return 0;

				var version = line.Substring(0, 3);
				switch (version)
				{
					case "V01":
						return 1;
					case "V03":
						return 3;
					default:
						return 0;
				}
			}
		}
		public static FlatFile OpenReader(string filename, FlatFileRow rowFormat = null, Encoding encoding = null, bool loadFmtFile=false, string fmtFile=null )
		{
			encoding = encoding ?? Encoding.ASCII;

			var result = new FlatFile
			{
				Encoding = encoding,
				RowFormat = rowFormat ?? new FlatFileRow(),
				FileName = filename,
				Buffer = File.OpenRead(filename)
			};

			result._reader = new StreamReader(result.Buffer, encoding);

			if (!string.IsNullOrEmpty(fmtFile) || loadFmtFile)
			{
				result.LoadFmtFile(fmtFile);
			}
			
			return result;
		}

		public static FlatFile OpenWriter(string filename, bool append = false, FlatFileRow rowFormat = null, Encoding encoding = null, bool loadFmtFile = false, string fmtFile = null)
		{
			encoding = encoding ?? Encoding.ASCII;
			var path = Path.GetDirectoryName(filename);
			Helpers.Net.Extensions.IO.TryCreateDirectory(path);

			var result = new FlatFile
			{
				Encoding = encoding,
				RowFormat = rowFormat ?? new FlatFileRow(),
				FileName = filename,
				Buffer = File.OpenWrite(filename)
			};

			result._writer = new StreamWriter(result.Buffer, encoding);
			
			if (!string.IsNullOrEmpty(fmtFile) || loadFmtFile)
			{
				result.LoadFmtFile(fmtFile);
			}

			return result;
		}

		public static void SaveFlatFile(string filename, IEnumerable<SharpObject> rows, string fmtFile=null, FlatFileRow rowFormat=null)
		{
			using (var file = OpenWriter(filename, rowFormat: rowFormat, fmtFile: fmtFile, loadFmtFile: !string.IsNullOrEmpty(fmtFile)))
			{
				file.Write(rows);

				fmtFile = fmtFile ?? Path.ChangeExtension(filename, ".fmt");
				if (!File.Exists(fmtFile))
					file.RowFormat.SaveFormatFile(fmtFile);
			}
		}

		public static Dictionary<string, string> Split(string filename, string splitOnField, FlatFileRow rowFormat, string sortOnField = null,
			int keySpan = 32, int splitCount = 100000, Encoding encoding = null, string indexField = null,
			bool keepSplitFiles = false, bool copyFmtFile = true)
		{
			encoding = encoding ?? Encoding.ASCII;

			var splitFiles = new Dictionary<string, string>();
			var splitWriters = new Dictionary<string, StreamWriter> ();

			var fileroot = Path.Combine(Path.GetDirectoryName(filename)??"", Path.GetFileNameWithoutExtension(filename)??"");

			try
			{
				using (var reader = new StreamReader(filename, encoding))
				{
					while (!reader.EndOfStream)
					{
						var line = reader.ReadLine();
						if (!string.IsNullOrEmpty(line))
						{
							var key = rowFormat.GetFieldValue(splitOnField, line);

							StreamWriter writer;
							if (!splitWriters.TryGetValue(key, out writer))
							{
								var splitFile = string.Format("{0}.{1}.dat", fileroot, key);
								var splitFmtFile = string.Format("{0}.{1}.fmt", fileroot, key);

								rowFormat.TrySaveFormatFile(splitFmtFile, encoding);

								writer = new StreamWriter(splitFile, false, encoding);
								splitFiles[key] = splitFile;
								splitWriters[key] = writer;
							}

							writer.WriteLine(line);
						}
					}
				}
			}
			finally
			{
				foreach (var writer in splitWriters.Values)
					writer.Close();
			}

			if (sortOnField != null)
			{
				Func<string,string> sort = line => rowFormat.GetFieldValue(sortOnField, line);

				foreach (var file in splitFiles.Values)
				{
					Sort(file, file, rowFormat, sort, keySpan,splitCount,encoding,indexField,keepSplitFiles);
				}
			}

			return splitFiles;
		}

	    public static int Count(string filename, FlatFileRow rowFormat, Encoding encoding = null)
	    {
            encoding = encoding ?? Encoding.ASCII;
            using(var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(stream, encoding))
            {
                int count = 0;
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrEmpty(line)) count++;
                }
                return count;
            }
        }

		public static void Sort(string filename, string outputFile, FlatFileRow rowFormat, Func<string, string> selectKey, int keySpan = 32, int splitCount = 100000, Encoding encoding = null, string indexField=null, bool keepSplitFiles=false)
		{
			encoding = encoding ?? Encoding.ASCII;

			IndexWriter indexWriter = new IndexWriter(encoding);
			indexWriter.KeySpan = keySpan;
			indexWriter.RowSpan = rowFormat.RowSpan + keySpan;

			var rows = new List<byte[]>();
			int splitFileCount = 0;
			string splitFileName = Path.Combine(Path.GetDirectoryName(outputFile)??"", String.Format("{0}.split{{0}}.dat", Path.GetFileNameWithoutExtension(outputFile)));

			List<string> splitFiles = new List<string>();
			using (var reader = new StreamReader(filename, encoding))
			{
				while (!reader.EndOfStream)
				{
					var line = reader.ReadLine();
					if (!string.IsNullOrEmpty(line))
					{
						var key = selectKey(line);
						rows.Add(indexWriter.Encode(key, line));
					}

					if (rows.Count >= splitCount || reader.EndOfStream)
					{
						rows.Sort(indexWriter.CompareByteArray);
						splitFileCount++;
						var splitFile = string.Format(splitFileName, splitFileCount);

						using (var stream = new FileStream(splitFile, FileMode.Create, FileAccess.ReadWrite))
						using (var writer = new BinaryWriter(stream))
						{
							foreach (var row in rows)
								writer.Write(row);
						}

						splitFiles.Add(splitFile);
						rows.Clear();
					}
				}
			}

			using (var stream = new FileStream(outputFile, FileMode.Create, FileAccess.ReadWrite))
			using (var writer = new BinaryWriter(stream))
			using (var indexSet = new IndexReaderSet { RowSpan = indexWriter.RowSpan, KeySpan = indexWriter.KeySpan })
			{
				indexSet.FileNames.AddRange(splitFiles);
				int count = 1;
				foreach (var row in indexSet.Select())
				{
					if (indexField != null)
						rowFormat.ReplaceField(indexField, string.Format("{0:D8}", count), row, indexSet.KeySpan);
					count++;
					writer.Write(row, indexSet.KeySpan, row.Length - indexSet.KeySpan);
					writer.Write('\r');
					writer.Write('\n');
				}
			}

			if (!keepSplitFiles)
			{
				foreach (var file in splitFiles)
				{
					File.Delete(file);
				}
			}
		}
	}

}
