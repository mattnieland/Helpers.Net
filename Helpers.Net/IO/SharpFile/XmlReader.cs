using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class XmlReader : IEnumerator<SharpRow>, ISharpRowReader
	{
		private Stream _source;
		private System.Xml.XmlReader _reader;

		private SharpRow _current;
	
		private SharpNodeCollection _nodes;
		private readonly StringBuilder _valueContent = new StringBuilder();
		private bool _hasContent = false;
		private bool _parseNode = false;
		private bool _hasAttributes = false;

		private bool _isEmpty = false;

		private List<string> _fullPath = new List<string>();
		private SharpNode _node = null;
		private List<SharpRow> _rowCache = new List<SharpRow>();
 
		public XmlReader(Stream source,  SharpNodeCollection nodes = null)
		{
			_source = source;
			_nodes = nodes;
		}

        public XmlReader(Stream source, SharpNodeCollection nodes, Encoding encoding)
        {
            _source = source;
            _nodes = nodes;

            var streamReader = new StreamReader(source, encoding);
            var settings = new System.Xml.XmlReaderSettings();

            _reader = System.Xml.XmlReader.Create(streamReader, settings);
        }

        private void OpenReader()
		{
			var settings = new System.Xml.XmlReaderSettings();
			_reader = System.Xml.XmlReader.Create(_source, settings);
		}

		private List<SharpRow> ParseNextNode()
		{
			List<SharpRow> rows = new List<SharpRow>();

			switch (_reader.NodeType)
			{
				case System.Xml.XmlNodeType.Comment:
				{
					var lines = _reader.Value.Split('\n').Select(x => x.TrimEnd()).ToList();
					if (lines.Count > 0 && String.IsNullOrEmpty(lines.Last()))
					
						lines.RemoveAt(lines.Count - 1);
					
					foreach (var line in lines)
						rows.Add(new SharpMetaCommentRow {Value = line});
					
					break;
				}
				case System.Xml.XmlNodeType.XmlDeclaration:
					rows.Add(new SharpMetaParameterRow
					{
						Key = "XmlDeclaration",
						Value = new SharpValue(_reader.Value)
					});
					break;
				case System.Xml.XmlNodeType.DocumentType:
					rows.Add(new SharpMetaParameterRow
					{
						Key = "DocumentType",
						Value = new SharpValue(_reader.Value)
					});
					break;
				case System.Xml.XmlNodeType.ProcessingInstruction:
					rows.Add(new SharpMetaParameterRow
					{
						Key = _reader.Name,
						Value = new SharpValue(_reader.Value)
					});
					break;
				case System.Xml.XmlNodeType.SignificantWhitespace:
				case System.Xml.XmlNodeType.Whitespace:
				case System.Xml.XmlNodeType.CDATA:
				case System.Xml.XmlNodeType.Text:
					if (_parseNode)
					{
						_valueContent.Append(_reader.Value);
						_hasContent = true;
					}
					break;
				case System.Xml.XmlNodeType.Element:
					if (_node == null)
					{
						if (_fullPath.Count > 0)
						{
							_node = _nodes.GetOrCreateNode(_fullPath, false);
							rows.Add(new SharpNodeRow(_node));
						}
					}

					_fullPath.Add(_reader.Name);

					_isEmpty = _reader.IsEmptyElement;

					_node = null;
					_hasContent = false;
					_parseNode = true;
					_valueContent.Clear();
					_hasAttributes = false;

					if (_reader.HasAttributes)
					{
						_hasAttributes = true;
						_node = _nodes.GetOrCreateNode(_fullPath, false);
						rows.Add(new SharpNodeRow(_node));

						for (int i = 0; i < _reader.AttributeCount; i++)
						{
							_reader.MoveToAttribute(i);

							var attrPath = new List<string>(_fullPath);
							attrPath.Add(string.Format("@{0}", _reader.Name));
							var attrNode = _nodes.GetOrCreateNode(attrPath, false);
							rows.Add(new SharpValueRow(attrNode, _reader.Value));
						}
					}
					else if (_isEmpty)
					{
						_node = _nodes.GetOrCreateNode(_fullPath, true);
						rows.Add(new SharpValueRow(_node, null));
						_hasContent = false;
						_parseNode = false;
					}

					if (_isEmpty)
						goto case System.Xml.XmlNodeType.EndElement;

					break;

				case System.Xml.XmlNodeType.EndElement:
					if (_hasContent || _parseNode)
					{
						var val = _valueContent.ToString();
						if (_isEmpty || !_hasContent)
							val = null;

						if (_hasAttributes)
						{
							if (val != null)
							{
								var parent = _nodes.GetOrCreateNode(_fullPath, false);
								_node = _nodes.GetOrCreateNode(_fullPath, true, parent);
								rows.Add(new SharpValueRow(_node, val));
							}
						}
						else
						{
							_node = _nodes.GetOrCreateNode(_fullPath, true);
							rows.Add(new SharpValueRow(_node, val));
						}

						_hasContent = false;
					}
					_parseNode = false;
					if (_fullPath.Count > 0)
						_fullPath.RemoveAt(_fullPath.Count - 1);

					break;
			}

			return rows;
		}

		#region IDisposable
		public void Dispose()
		{
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
				OpenReader();

			if (_reader == null)
				return false;

			if (_rowCache.Count > 0)
			{
				_current = _rowCache[0];
				_rowCache.RemoveAt(0);
				return true;
			}

			bool valid = false;
			while (!valid)
			{
				if (!_reader.Read())
					return false;

				_rowCache.AddRange(ParseNextNode());
				if (_rowCache.Count > 0)
					valid = true;
			}

			if (_rowCache.Count > 0)
			{
				_current = _rowCache[0];
				_rowCache.RemoveAt(0);
				return true;
			}

			return false;
		}
	
		public void Reset()
		{
			_reader = null;	
			_source.Seek(0, SeekOrigin.Begin);
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

		public IEnumerator<SharpRow> GetEnumerator()
		{
			return this;
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return this;
		}
		#endregion
	}
}
