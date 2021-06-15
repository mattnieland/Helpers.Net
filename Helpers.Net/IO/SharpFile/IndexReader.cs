using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    public class IndexReader : IDisposable
	{
		private Stream _source;
		private BinaryReader _reader;
		private Encoding _encoding;
		private int _rowSpan = 32;
		private int _keySpan = 16;
		private bool _cloneRows = false;
		private bool _closeSource = false;
		private byte[] _current = null;
		private byte[] _currentKey = null;

		private bool _useVariableRowSpan = false;

		public IndexReader(string filename, Encoding encoding = null)
		{
			_source = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
			_closeSource = true;

			_encoding = encoding ?? Encoding.ASCII;
		}

		public IndexReader(Stream source, Encoding encoding = null)
		{
			_encoding = encoding ?? Encoding.ASCII;
			_source = source;
		}

		public byte[] Current
		{
			get
			{
				if (_current == null)
					MoveNext();

				return _current;
			}
		}

		public byte[] Key
		{
			get
			{
				if (_current == null)
					MoveNext();

				if (UseVariableRowSpan)
					return _currentKey;
				
				return _current;
			}
		}

		public bool MoveNext()
		{
			if (_reader == null)
			{
				_source.Seek(0, SeekOrigin.Begin);
				_reader = new BinaryReader(_source);
			}

			if (UseVariableRowSpan)
			{
				_currentKey = _reader.ReadBytes(_keySpan);
				if (_currentKey.Length < _keySpan) return false;

				int size = _reader.ReadInt32();
				_current = _reader.ReadBytes(size);
				return _current.Length == size;
			}
			
			_current = _reader.ReadBytes(_rowSpan);
			return _current.Length == _rowSpan;
		}

		public int CompareByteArray(byte[] a1, byte[] a2)
		{
			if (a1 == null || a2 == null)
				return 0;

			if (a1.Length != a2.Length)
				return a1.Length - a2.Length;

			for (int i = 0; i < a1.Length && i < _keySpan; i++)
			{
				if (a1[i] != a2[i])
					return a1[i] - a2[i];
			}

			return 0;
		}
		public IEnumerable<byte[]> Rows
		{
			get
			{
				_source.Seek(0, SeekOrigin.Begin);
				using (var reader = new BinaryReader(_source))
				{
					while (true)
					{
						if (UseVariableRowSpan)
						{
							var key = _reader.ReadBytes(_keySpan);
							var size = _reader.ReadInt32();
							var buff = reader.ReadBytes(size);
							if (buff.Length == _rowSpan)
							{
								yield return buff;
							}
							else
							{
								break;
							}
						}
						else
						{
							var buff = reader.ReadBytes(_rowSpan);
							if (buff.Length == _rowSpan)
							{
								yield return buff;
							}
							else
							{
								break;
							}	
						}
					}
				}
			}
		}

		public int RowSpan
		{
			get { return _rowSpan; }
			set { _rowSpan = value; }
		}

		public bool CloneRows
		{
			get { return _cloneRows; }
			set { _cloneRows = value; }
		}

		public int KeySpan
		{
			get { return _keySpan; }
			set { _keySpan = value; }
		}

		public bool UseVariableRowSpan
		{
			get { return _useVariableRowSpan; }
			set { _useVariableRowSpan = value; }
		}

		public IEnumerable<byte[]> Select(string key)
		{
			_source.Seek(0, SeekOrigin.Begin);
			var k = _encoding.GetBytes(key);
			var buff = new byte[RowSpan];

			using (var reader = new BinaryReader(_source, _encoding))
			{
				while (true)
				{
					var total = reader.Read(buff, 0, RowSpan);

					if (total != RowSpan)
						yield break;

					bool skipRow = false;
					for (int i = 0; i < k.Length; i++)
					{
						if (buff[i] != k[i])
						{
							skipRow = true;
							break;
						}
					}

					if (skipRow)
						continue;

					if (_cloneRows)
					{
						var row = new byte[RowSpan];
						Buffer.BlockCopy(buff, 0, row, 0, RowSpan);
						yield return row;
					}
					else
					{
						yield return buff;
					}
				}
			}
		}

		public void Dispose()
		{
			if (_closeSource && _source != null)
				_source.Close();

			_reader = null;
			_source = null;
		}

        public int LineCount()
        {
            var lineCount = 0;

            var streamReader = new StreamReader(_source);

            while (streamReader.ReadLine() != null) { lineCount++; }

            return lineCount;
        }
	}
}
