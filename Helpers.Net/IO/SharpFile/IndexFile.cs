using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{

	#region IndexTree
	
	public class IndexTree
	{
		public byte[] Key = null;
		public SortedList<byte, IndexTree> Nodes = new SortedList<byte, IndexTree>();

		public void Clear()
		{
			Nodes.Clear();
			Key = null;
		}
		
		public byte[] Select(string key)
		{
			return Select(Encoding.ASCII.GetBytes(key));
		}

		public byte[] First()
		{
			if (Key != null)
				return Key;

			var next = Nodes.FirstOrDefault();
			if (next.Value != null)
				return next.Value.First();

			return null;
		}

		public byte[] Select(byte[] key, int position = 0)
		{
			if (position >= key.Length)
				return Key;

			IndexTree next;
			byte[] result = Key;

			if (Nodes.TryGetValue(key[position], out next))
			{
				result = next.Select(key, position + 1);
				if (result != null)
					return result;
			}

			if (result == null)
			{
				var target = key[position];
				next = null;

				foreach (var node in Nodes)
				{
					if (node.Key < target)
						next = node.Value;
				}

				if (next != null)
					return next.First();
				else
					return Key;
			}
			
			return result;
		}

		public void Add(byte[] key, int position=0)
		{
			if (position >= key.Length)
			{
				Key = key;
			}
			else
			{
				IndexTree next;
				if (!Nodes.TryGetValue(key[position], out next))
				{
					next = new IndexTree();
					Nodes[key[position]] = next;

				}
				next.Add(key, position + 1);
			}
		}

		public void Add(string key)
		{
			Add(Encoding.ASCII.GetBytes(key));
		}
	}
	
	#endregion

	public class IndexFile : IDisposable
	{
		private int _rowSpan = 32;

		private string _tempFolder;
		private string _indexFolder;
		private Func<byte[], int> _indexSeed;

		private List<string> _changedFiles = new List<string>();
 
		private List<byte[]> _rows = new List<byte[]>();
		
		private IndexTree _index = new IndexTree();

		private Dictionary<byte[], IndexWriter> _indexWriter = new Dictionary<byte[], IndexWriter>();
		private Dictionary<byte[], IndexReader> _indexReader = new Dictionary<byte[], IndexReader>();

		private Encoding _defaultEncoding = Encoding.ASCII;

		public IndexFile(string indexFolder, string tempFolder=null)
		{
			_indexFolder = indexFolder;
			_tempFolder = tempFolder ?? Path.Combine(_indexFolder, "TEMP");
			
		}

		public int[] Select(string key)
		{
			return new int[0];
		}

		public string SelectFile(string key)
		{
			var bytes = _index.Select(key);
			return string.Format("_{0}.dat", Encoding.ASCII.GetString(bytes));
		}

		public int RowSpan
		{
			get { return _rowSpan; }
			set { _rowSpan = value; }
		}

		public string TempFolder
		{
			get { return _tempFolder; }
			set { _tempFolder = value; }
		}

		public string IndexFolder
		{
			get { return _indexFolder; }
			set { _indexFolder = value; }
		}

		public List<byte[]> Rows
		{
			get { return _rows; }
			set { _rows = value; }
		}

		public List<string> ChangedFiles
		{
			get { return _changedFiles; }
			set { _changedFiles = value; }
		}

		public Func<byte[], int> IndexSeed
		{
			get { return _indexSeed; }
			set { _indexSeed = value; }
		}

		public void UpdateIndex()
		{
			_rows.Sort(CompareRows);

			LoadIndex();

			foreach (var row in _rows)
			{
				var key = SelectIndexKey(row);

				var reader = _indexReader[key];
				var writer = _indexWriter[key];

				if (reader != null)
				{
					bool readerEnd = false;
					int cr = CompareRows(reader.Current, row);

					while (cr < 0)
					{
						writer.Write(reader.Current);
						if (!reader.MoveNext())
						{
							readerEnd = true;
							break;
						}
						cr = CompareRows(reader.Current, row);
					}

					if (cr == 0)
					{
						if (!reader.MoveNext())
							readerEnd = true;
					}

					if (readerEnd)
					{
						reader.Dispose();
						_indexReader[key] = null;
					}

					writer.Write(row);
				}
				else
				{
					writer.Write(row);
				}
			}

			SubmitChanges();
		}

		public void SubmitChanges()
		{
			foreach (var item in _indexReader)
			{
				if (item.Value == null)
					continue;

				var writer = _indexWriter[item.Key];
				var reader = item.Value;

				if (reader.Current != null && reader.Current.Length == _rowSpan)
					writer.Write(reader.Current);

				while(reader.MoveNext())
					writer.Write(reader.Current);

				reader.Dispose();
			}
				
			foreach (var writer in _indexWriter.Values)
				writer.Close();
			
			_indexWriter.Clear();
			_indexReader.Clear();

			
			foreach (var file in Directory.EnumerateFiles(_tempFolder))
			{
				var destFile = Path.Combine(_indexFolder, Path.GetFileName(file)??"");
				_changedFiles.Add(destFile);
				File.Copy(file, destFile, true);
				File.Delete(file);
			}

			var masterFile = Path.Combine(_indexFolder, "index.txt");
			File.WriteAllLines(masterFile, Directory.EnumerateFiles(_indexFolder).Where(x=>x.EndsWith(".dat")).Select(Path.GetFileName));
			
			_changedFiles.Add(masterFile);
			_changedFiles = _changedFiles.Distinct().ToList();
			
		}

		public void SplitIndex()
		{
			
		}

		public void Dispose()
		{
			foreach (var writer in _indexWriter.Values)
				writer.Close();

			foreach(var reader in _indexReader.Values.Where(x=>x!=null))
				reader.Dispose();

			_indexWriter.Clear();
			_indexReader.Clear();
			_rows.Clear();
			_index.Clear();
		}

		public void Add(string sourceFile)
		{
			Add(new List<string> {sourceFile});
		}

		public void Add(IEnumerable<string> sourceFiles)
		{

			foreach (var source in sourceFiles)
			{
				using (var reader = new IndexReader(source))
				{
					reader.RowSpan = _rowSpan;
					_rows.AddRange( reader.Rows );
				}
			}
		}

		private int CompareRows(byte[] first, byte[] second)
		{
			for (int i = 0; i < _rowSpan; i++)
			{
				var a = first[i];
				var b = second[i];
				if (a < b) return -1;
				if (a > b) return 1;
			}

			return 0;
		}

		public void LoadIndex(bool alwaysRefresh = false)
		{
			_indexReader.Clear();
			_indexWriter.Clear();
			_index.Clear();

			var masterFile = Path.Combine(_indexFolder, "index.txt");
			if (!File.Exists(masterFile) || alwaysRefresh)
			{
				if (File.Exists(masterFile))
					File.Delete(masterFile);

				File.WriteAllLines(masterFile,
					Directory.EnumerateFiles(_indexFolder).Where(x => x.EndsWith(".dat")).Select(Path.GetFileName));
			}

			foreach (var line in File.ReadAllLines(masterFile))
			{
				_index.Add(line.ToUpper().Replace(".DAT", "").Trim('_'));
			}
		}

		private byte[] ValidateKey(byte[] key, byte[] row)
		{
			bool isMatch = true;
			var matchLength = _indexSeed == null ? 1 : _indexSeed(row);

			if (key != null && key.Length >= matchLength)
			{
				for (int i = 0; i < matchLength; i++)
				{
					if (key[i] != row[i])
					{
						isMatch = false;
						break;
					}
				}
			}
			else
			{
				isMatch = false;
			}

			if (!isMatch)
			{
				key = row.Take(matchLength).ToArray();
				_index.Add(key);
			}

			return key;
		}

		private byte[] SelectIndexKey(byte[] row)
		{
			var key = _index.Select(row);
			key = ValidateKey(key, row);

			if (_indexReader.ContainsKey(key) && _indexWriter.ContainsKey(key))
				return key;

			var filename = string.Format("_{0}.dat", _defaultEncoding.GetString(key));
			Helpers.Net.Extensions.IO.TryCreateDirectory(_indexFolder);
			Helpers.Net.Extensions.IO.TryCreateDirectory(_tempFolder); 
			
			var readerFile = Path.Combine(_indexFolder, filename);
			var writerFile = Path.Combine(_tempFolder, filename);
			

			if (File.Exists(readerFile))
			{
				_indexReader[key] = new IndexReader(readerFile, _defaultEncoding) {RowSpan = _rowSpan};
			}
			else
				_indexReader[key] = null;

			_indexWriter[key] = new IndexWriter(writerFile) { RowSpan = _rowSpan};

			return key;
		}
	}
}
