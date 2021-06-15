using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class ConfigReader : IEnumerator<SharpRow>, ISharpRowReader
	{
		private bool _cacheSource = false;
		private readonly List<string> _sourceLines = new List<string>(); 

		private Stream _source;
		private StreamReader _reader;
		private Encoding _encoding;

		private int _row = 0;
		private SharpRow _current;

		private string _lastFullPath = "";

		private SharpNodeCollection _nodes;

		public ConfigReader(Stream source,  SharpNodeCollection nodes = null, Encoding encoding = null)
		{
			_encoding = encoding ?? Encoding.UTF8;
			_source = source;
			_nodes = nodes;
		}

		public void ReadToEnd()
		{
			foreach (var row in this)
			{
			}
		}

		public bool ParseRowLine(string line, out SharpRow result)
		{
			result = null; 
			line = line.Trim();
			if (string.IsNullOrEmpty(line))
				return false;

			if (line.StartsWith("#!"))
			{
				var param = new SharpMetaParameterRow(line);
				if (param.Key.ToLower() == "package" && _nodes != null)
				{
					_nodes.SetCurrentPackage(param.Value.ToString());
				}
				result = param;
				return true;
			}
			
			if (line.StartsWith("#"))
			{
				result = new SharpMetaCommentRow {Value = line.Substring(1)};
				return true;
			}

			var metaNode = new SharpMetaNodeRow(line, _lastFullPath);
			if (line.StartsWith("$"))
			{
				var schemaNode = metaNode.CreateNode();
				
				if (metaNode.Index == -1)
					_nodes.Add(schemaNode);
				else
					_nodes.Insert(schemaNode);

				return false;
			}

			bool isValueNode = metaNode.IsValueNode;

			if (!isValueNode)
				_lastFullPath = metaNode.Path;

			if (_nodes == null)
			{
				result = metaNode;
				return true;
			}

			var node = _nodes.GetNode(metaNode.Path);
			if (node != null)
			{
				result = metaNode.CreateNodeRow(node);
				return true;
			}

			node = metaNode.CreateNode();
			_nodes.Add(node);
			result = metaNode.CreateNodeRow(node);
			return true;
		}
		
		#region IDisposable
		public void Dispose()
		{
			_sourceLines.Clear();
			_reader = null;
			_source = null;
		}
		#endregion

		#region IEnumerator

		public bool MoveNext()
		{
			if (_source == null)
				return false;

			if (_reader == null)
				_reader = new StreamReader(_source, _encoding);


			string line;
			bool valid = false;
			while (!valid)
			{
				if (_row >= _sourceLines.Count)
				{
					if (_reader.EndOfStream)
						return false;

					line = _reader.ReadLine();
					if (_cacheSource)
						_sourceLines.Add(line);
				}
				else
				{
					line = _sourceLines[_row];
				}

				if (!ParseRowLine(line, out _current))
				{
					_row ++;
				}
				else
				{
					valid = true;
					_row++;
				}
			}
			
			return true;
		}

		public void Reset()
		{
			if (_cacheSource)
			{
				_row = 0;
			}
			else
			{
				_row = 0;
				_source.Seek(0, SeekOrigin.Begin);
			}
		}

		public SharpRow Current
		{
			get { return _current; }
		}

		object IEnumerator.Current
		{
			get { return Current; }
		}
		#endregion

		#region IEnumerable

		public IEnumerator<SharpRow>  GetEnumerator()
		{
 			return this;
		}

		IEnumerator  IEnumerable.GetEnumerator()
		{
			return this;
		}
		#endregion
}

}
