using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
	public class IndexReaderSet : IDisposable
	{
		public int RowSpan = 32;
		public int KeySpan = 16;

		public bool CloneRows = false;
		public Encoding Encoding = Encoding.ASCII;
		public List<string> FileNames = new List<string>();

		private List<IndexReader> _readers = new List<IndexReader>();

		private bool _useVariableRowSpan = false;

		public bool UseVariableRowSpan
		{
			get { return _useVariableRowSpan; }
			set { _useVariableRowSpan = value; }
		}

		public IEnumerable<List<byte[]>> SelectFiles(params string[] additionalInfoFiles)
		{
			List<bool> additionalReaderValid = new List<bool>();
			List<IndexReader> additionalReaders = new List<IndexReader>();
			
			foreach (var filename in additionalInfoFiles)
			{
				var r = new IndexReader(filename)
				{
					KeySpan = this.KeySpan,
					RowSpan = this.RowSpan,
					UseVariableRowSpan = this.UseVariableRowSpan
				};
				
				additionalReaders.Add(r);
				additionalReaderValid.Add(r.MoveNext());
			}

			var empty = new byte[0];

			foreach (var item in SelectPairs())
			{
				var result = new List<byte[]> {item.Value};

				for(int i=0;i<additionalReaders.Count;i++)
				{
					var v = additionalReaderValid[i];
					if (!v)
					{
						result.Add(empty);
						continue;
					}

					var r = additionalReaders[i];
					var c = r.CompareByteArray(r.Key, item.Key);
					while (v && c < 0)
					{
						v = r.MoveNext();
						if (v) c = r.CompareByteArray(r.Key, item.Key);
					}

					if (!v)
					{
						additionalReaderValid[i] = false;
						result.Add(empty);
					}
					else if (c == 0)
						result.Add(r.Current);
					else
						result.Add(empty);
				}

				yield return result;
			}
		}

		private void InsertReader(IndexReader reader, int startPos = 0)
		{
			int i;
			for (i = startPos; i < _readers.Count; i++)
			{
				if (reader.CompareByteArray(reader.Key, _readers[i].Key) <= 0)
					break;
			}
			if (i < _readers.Count)
				_readers.Insert(i, reader);
			else
				_readers.Add(reader);
		}

		public IEnumerable<KeyValuePair<byte[], byte[]>> SelectPairs(string key = null)
		{
			foreach (var reader in _readers)
				reader.Dispose();
			_readers.Clear();

			for (int i = FileNames.Count - 1; i >= 0; i--)
			{
				var reader = new IndexReader(FileNames[i], Encoding) { RowSpan = RowSpan, KeySpan = KeySpan, CloneRows = CloneRows, UseVariableRowSpan = UseVariableRowSpan };
				if (reader.MoveNext())
				{
					InsertReader(reader);
				}
				else
				{
					reader.Dispose();
				}
			}

			while (_readers.Count > 0)
			{
				var reader = _readers[0];
				yield return new KeyValuePair<byte[], byte[]>(reader.Key, reader.Current);

				if (!reader.MoveNext())
				{
					reader.Dispose();
					_readers.RemoveAt(0);
					continue;
				}

				if (_readers.Count == 1)
					continue;

				if (reader.CompareByteArray(reader.Key, _readers[1].Key) <= 0)
					continue;

				_readers.RemoveAt(0);
				InsertReader(reader, 1);
			}
		}

		public IEnumerable<byte[]> Select(string key = null)
		{
			foreach (var reader in _readers)
				reader.Dispose();
			_readers.Clear();

			for (int i=FileNames.Count-1;i>=0;i--)
			{
				var reader = new IndexReader(FileNames[i], Encoding) {RowSpan = RowSpan, KeySpan = KeySpan, CloneRows = CloneRows, UseVariableRowSpan = UseVariableRowSpan};
				if (reader.MoveNext())
				{
					InsertReader(reader);
				}
				else
				{
					reader.Dispose();
				}
			}

			while (_readers.Count > 0)
			{
				var reader = _readers[0];
				yield return reader.Current;
				if (!reader.MoveNext())
				{
					reader.Dispose();
					_readers.RemoveAt(0);
					continue;
				}

				if (_readers.Count == 1)
					continue;
				
				if (reader.CompareByteArray(reader.Key, _readers[1].Key) <= 0)
					continue;

				_readers.RemoveAt(0);
				InsertReader(reader, 1);
			}
		}

		public void Dispose()
		{
			foreach(var reader in _readers)
				reader.Dispose();

			_readers.Clear();
		}
	}
}
