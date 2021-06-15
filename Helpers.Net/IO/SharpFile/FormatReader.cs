using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class FormatReader
	{
		private readonly List<SharpNodeMapColumn> _columns = new List<SharpNodeMapColumn>();
		private Dictionary<string, string> _names = new Dictionary<string, string>();
		private readonly Dictionary<string, SharpValueType> _types = new Dictionary<string, SharpValueType>();

		private readonly Dictionary<string, char> _padding = new Dictionary<string, char>();
		private readonly Dictionary<string, string> _format = new Dictionary<string, string>();
		private readonly Dictionary<string, bool> _allowTrim = new Dictionary<string, bool>();
		private readonly Dictionary<string, bool> _alignLeft = new Dictionary<string, bool>();

		private Stream _source;
		private Encoding _encoding;

		public FormatReader(Stream source, Encoding encoding = null)
		{
			_source = source;
			_encoding = encoding ?? Encoding.UTF8;
		}

		public static SharpNodeMap LoadHeaderLine(string line, char delimiter, SharpNodeCollection nodes, string path, Dictionary<string, string> aliases = null)
		{
			SharpNode rootNode = nodes.GetNode(path);

			if (rootNode == null)
			{
				rootNode = new SharpNode
				{
					Path = path,
					Name = SharpNode.GetNodeName(path),
				};
				nodes.Add(rootNode);
			}

			SharpNodeMap result = new SharpNodeMap();
			result.MapType = SharpMapType.Variable;

			var columns = line.Split(delimiter).Select(x => x.Trim().Trim('"')).ToList();

			foreach (var column in columns)
			{
				var colname = column;
				if (aliases != null)
				{
					if (aliases.ContainsKey(colname))
						colname = aliases[colname];
				}
				var nodePath = string.Format("{0}/{1}/#", path, colname);
				var colNode = nodes.GetNode(nodePath);
				if (colNode == null)
				{
					colNode = new SharpNode
					{
						Path = nodePath,
						Name = colname,
						IsValueNode = true,
						IsLeafNode = true
					};
					nodes.Add(colNode);
				}

				result.Columns.Add(new SharpNodeMapColumn(colNode) { FieldName = column });
			}

			string mapPath = string.Join(",", result.Columns.Select(c => c.Node.Index));

			var mapNode = new SharpNodeMap
			{
				Path = mapPath,
				IsValueNode = true,
				MapType = SharpMapType.Variable,
				Delimiter = delimiter,
			};

			mapNode.SetColumns(result.Columns);
			nodes.Add(mapNode);

			if (nodes.GetNode(mapPath) == null)
				nodes.Add(mapNode);

			return mapNode;
		}

		public static SharpNodeMap LoadFormatFile(string filename, SharpNodeCollection nodes, string path, Encoding encoding = null, Dictionary<string, string> aliases = null)
		{
			SharpNodeMap result;

			using (var stream = new FileStream(filename, FileMode.Open))
			{
				var reader = new FormatReader(stream, encoding);
				if (aliases != null)
					reader.Names = aliases;
				result = reader.CreateColumnNodes(nodes, path);
			}

			return result;
		}

		public Dictionary<string, string> Format
		{
			get { return _format; }
		}

		public Dictionary<string, bool> AllowTrim
		{
			get { return _allowTrim; }
		}

		public Dictionary<string, bool> AlignLeft
		{
			get { return _alignLeft; }
		}

		public Dictionary<string, SharpValueType> Types
		{
			get { return _types; }
		}

		public Dictionary<string, string> Names
		{
			get { return _names; }
			set { _names = value; }
		}

		public List<SharpNodeMapColumn> Columns
		{
			get { return _columns; }
		}

		public Dictionary<string, char> Padding
		{
			get { return _padding; }
		}

		public SharpNodeMap CreateColumnNodes(SharpNodeCollection nodes, string path, bool alwaysUpdateColumns = true)
		{
			SharpNode rootNode = nodes.GetNode(path);

			if (rootNode == null)
			{
				rootNode = new SharpNode
				{
					Path = path,
					Name = SharpNode.GetNodeName(path),
				};
				nodes.Add(rootNode);
			}

			using (var reader = new StreamReader(_source, _encoding))
			{
				int position = 0;
				while (!reader.EndOfStream)
				{
					var line = reader.ReadLine();
					if (line == null)
						continue;

					line = line.Trim();
					if (string.IsNullOrEmpty(line))
						continue;

					var parts = line.Split(',').Select(x => x.Trim()).ToList();

					string fieldName = parts[0];
					string columnName = fieldName;
					if (_names.ContainsKey(columnName))
						columnName = _names[columnName];

					if (columnName == "EOR")
						continue;

					string columnPath = string.Format("{0}/{1}/#", rootNode.Path, columnName);
					int columnWidth;
					int.TryParse(parts[1], out columnWidth);

					var columnNode = nodes.GetNode(columnPath);

					if (columnNode == null)
					{
						columnNode = new SharpNode
						{
							Path = columnPath,
							Name = columnName,
							IsValueNode = true,
							ValueType = _types.ContainsKey(columnName) ? _types[columnName] : SharpValueType.None,
						};

						nodes.Add(columnNode);
					}

					var column = new SharpNodeMapColumn(columnNode, columnWidth, position);
					column.FieldName = fieldName;

					if (_allowTrim.ContainsKey(columnName))
						column.AllowTrim = _allowTrim[columnName];

					if (_alignLeft.ContainsKey(columnName))
						column.AlignLeft = _alignLeft[columnName];

					if (_format.ContainsKey(columnName))
						column.Format = _format[columnName];

					if (_padding.ContainsKey(columnName))
						column.Padding = _padding[columnName];

					position += columnWidth;
					_columns.Add(column);
				}
			}

			string mapPath = string.Join(",", _columns.Select(c => c.Node.Index));

			var mapNode = new SharpNodeMap
			{
				Path = mapPath,
				IsValueNode = true
			};

			mapNode.SetColumns(_columns);
			mapNode.MapType = SharpMapType.Fixed;

			if (nodes.GetNode(mapPath) == null)
				nodes.Add(mapNode);
			else
			{
				mapNode.Parent = nodes.GetNode(mapPath).Parent;
			}
			return mapNode;
		}
	}
}
