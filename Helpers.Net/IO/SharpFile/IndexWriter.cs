using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
	public class IndexWriter : IDisposable
	{
		private Stream _dest;
		private BinaryWriter _writer = null;
		private Encoding _encoding;
		private bool _closeWriter = false;

		private int _rowSpan = 32;
		private int _keySpan = 16;

		private bool _useVariableRowSpan = false;

		private List<byte[]> _emptySpace = new List<byte[]>();

		public IndexWriter(Encoding encoding = null)
		{
			_encoding = encoding ?? Encoding.ASCII;
			InitEmptySpace();
		}

		public IndexWriter(string filename, bool append = false, Encoding encoding = null)
		{
			_dest = new FileStream(filename, append ? FileMode.Append : FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
			_writer = new BinaryWriter(_dest);
			_closeWriter = true;
			_encoding = encoding ?? Encoding.ASCII;
			InitEmptySpace();
		}

		public IndexWriter(Stream dest, Encoding encoding = null)
		{
			_dest = dest;
			_writer = new BinaryWriter(_dest);
			_encoding = encoding ?? Encoding.ASCII;
			InitEmptySpace();
		}

		public int CompareByteArray(byte[] a1, byte[] a2)
		{
			if (a1 == null || a2 == null)
				return 0;

			if (!_useVariableRowSpan || a2.Length < _keySpan || a1.Length < _keySpan)
			{
				if (a1.Length != a2.Length)
					return a1.Length - a2.Length;
			}

			for (int i = 0; i < a1.Length && i < _keySpan && i < a2.Length; i++)
			{
				if (a1[i] != a2[i])
					return a1[i] - a2[i];
			}

			return 0;
		}

		public int RowSpan
		{
			get { return _rowSpan; }
			set
			{
				_rowSpan = value;
				InitEmptySpace();
			}
		}

		public int KeySpan
		{
			get { return _keySpan; }
			set
			{
				_keySpan = value;
				InitEmptySpace();
			}
		}

		public bool UseVariableRowSpan
		{
			get { return _useVariableRowSpan; }
			set { _useVariableRowSpan = value; }
		}

		private void InitEmptySpace()
		{
			_emptySpace.Clear();
			int size = RowSpan > _keySpan ? _rowSpan : _keySpan;
			for (int i = 0; i < size; i++)
			{
				var buff = new byte[i];
				for (int j = 0; j < i; j++)
					buff[j] = 32;

				_emptySpace.Add(buff);
			}
		}

		public void Flush()
		{
			if (_writer != null)
				_writer.Flush();
		}

		public void Close()
		{
			Dispose();
		}

		public void Dispose()
		{
			Flush();

			if (_closeWriter && _dest != null)
				_dest.Close();

			_dest = null;
			_writer = null;
		}
		public void Write(byte[] row)
		{
			_writer.Write(row);
		}

		public byte[] Encode(string key, string value)
		{
			return Encode(_encoding.GetBytes(key), _encoding.GetBytes(value));
		}

		public byte[] Encode(byte[] key, byte[] value)
		{
			if (_useVariableRowSpan)
			{
				if (key.Length > _keySpan)
					throw new InvalidDataException(string.Format("Key exceeded {0} bytes: Found {1}", KeySpan, key.Length));

				var result = new byte[_keySpan + 4 + value.Length];
				int offset = 0;

				var space = _keySpan - key.Length;
				Buffer.BlockCopy(key, 0, result, offset, key.Length);
				offset += key.Length;
				if (space > 0)
				{
					Buffer.BlockCopy(_emptySpace[space], 0, result, offset, space);
					offset += space;
				}
				int size = value.Length;
				Buffer.BlockCopy(BitConverter.GetBytes(size), 0, result, offset, 4);
				offset += 4;
				Buffer.BlockCopy(value, 0, result, offset, value.Length);
				return result;
			}
			else
			{
				var space = _rowSpan - key.Length - value.Length;
				if (space < 0)
					throw new InvalidDataException(string.Format("Row exceeded {0} bytes: Found {1}", _rowSpan,
						key.Length + value.Length));

				var result = new byte[_rowSpan];
				int offset = 0;
				Buffer.BlockCopy(key, 0, result, offset, key.Length);
				offset += key.Length;
				Buffer.BlockCopy(_emptySpace[space], 0, result, offset, space);
				offset += space;
				Buffer.BlockCopy(value, 0, result, offset, value.Length);

				return result;
			}
		}

		public void Write(string key, string value)
		{
			Write(_encoding.GetBytes(key), _encoding.GetBytes(value));
		}

		public void Write(string key, byte[] value)
		{
			Write(_encoding.GetBytes(key), value);
		}

		public void Write(string key, params int[] values)
		{
			var keyBytes = _encoding.GetBytes(key);

			if (_useVariableRowSpan)
			{
				if (keyBytes.Length > _keySpan)
					throw new InvalidDataException(string.Format("Key exceeded {0} bytes: Found {1}", _keySpan, keyBytes.Length));

				var space = _keySpan - keyBytes.Length;

				_writer.Write(keyBytes);
				if (space > 0)
					_writer.Write(_emptySpace[space]);

				int size = values.Length * 4;
				_writer.Write(size);
			}
			else
			{
				var space = _rowSpan - keyBytes.Length - values.Length * 4;
				if (space < 0)
					throw new InvalidDataException(string.Format("Row exceeded {0} bytes: Found {1}", _rowSpan, keyBytes.Length + values.Length * 4));
				_writer.Write(keyBytes);
				_writer.Write(_emptySpace[space]);
			}

			foreach (int val in values)
				_writer.Write(val);
		}

		public void Write(byte[] key, byte[] value)
		{
			if (_useVariableRowSpan)
			{
				if (key.Length > _keySpan)
					throw new InvalidDataException(string.Format("Key exceeded {0} bytes: Found {1}", _keySpan, key.Length));

				var space = _keySpan - key.Length;
				_writer.Write(key);
				if (space > 0)
					_writer.Write(_emptySpace[space]);

				int size = value.Length;
				_writer.Write(size);
			}
			else
			{
				var space = _rowSpan - key.Length - value.Length;

				if (space < 0)
					throw new InvalidDataException(string.Format("Row exceeded {0} bytes: Found {1}", _rowSpan, key.Length + value.Length));

				_writer.Write(key);
				_writer.Write(_emptySpace[space]);
			}
			_writer.Write(value);
		}
	}
}
