using Helpers.Net.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpNodeMap : SharpNode
	{
		private List<SharpNodeMapColumn> _columns = new List<SharpNodeMapColumn>();
		private List<SharpValue> _cachedRow = null;
		private List<SharpNode> _cachedNodes = null;
 
		public SharpMapType MapType = SharpMapType.None;
		public char Delimiter = ',';
		public override bool IsSingleValueNode { get { return false; }}

		public List<SharpNodeMapColumn> Columns
		{
			get { return _columns; }
			set { _columns = value; }
		}

		public SharpNodeMap()
		{  
		}

		public SharpNodeMap(IEnumerable<SharpValueRow> columns)
		{
			_columns = columns.Select(x => new SharpNodeMapColumn(x.Node)).ToList();
			MapType = SharpMapType.Sequence;
		}

		public SharpNodeMap(IEnumerable<SharpNode> columns)
		{
			_columns = columns.Select(x => new SharpNodeMapColumn(x)).ToList();
			MapType = SharpMapType.Sequence;
		}

		public void SetColumns(IEnumerable<SharpNodeMapColumn> columns)
		{
			_columns = new List<SharpNodeMapColumn>(columns);
			_cachedNodes = null;
		}

		public override IEnumerable<SharpNode> GetNodes()
		{
			if (_cachedNodes == null)
				_cachedNodes = _columns.Select(x=>x.Node ?? Nodes.GetNode(x.Index)).ToList();

			return _cachedNodes;
		}

		public IEnumerable<SharpValue> ExpandRow(string value, bool useCache = true)
		{
			if (MapType == SharpMapType.Fixed)
				return GetFixedRow(value, useCache);

			if (MapType == SharpMapType.Variable)
				return GetVariableRow(value, useCache);

			var rows = useCache && _cachedRow != null ? _cachedRow : GetColumnValues();
			return rows;
		}

		private IEnumerable<SharpValue> GetVariableRow(string value, bool useCache = true)
		{
			var rows = useCache && _cachedRow != null ? _cachedRow : GetColumnValues();

			int charpos = 0;
			for (int pos = 0; pos < _columns.Count && charpos < value.Length; pos++)
			{
				var startpos = charpos;
				
				bool quoted = false;
				bool escaped = false;
				bool hadquotes = false;

				while (charpos < value.Length && !(!quoted && !escaped && (value[charpos] == Delimiter)))
				{
					var c = value[charpos];
					charpos++;
					
					if (quoted)
					{
						if (escaped)
						{
							escaped = false;
							continue;
						}

						if (c == '\\')
						{
							escaped = true;
							continue;
						}

						if (c == '"')
							quoted = false;

						continue;
					}
					
					if (c == '"')
					{
						quoted = true;
						hadquotes = true;
					}
					
				}

				var val = value.Substring(startpos, charpos - startpos);
				if (hadquotes)
					val = EncodedString.ParseQuotedString(val);

				rows[pos].Value = val;
				
				charpos++;
			}

			return rows;
		}

		private IEnumerable<SharpValue> GetFixedRow(string value, bool useCache = true)
		{
			var rows = useCache && _cachedRow != null ? _cachedRow : GetColumnValues();

			for (int pos = 0; pos < _columns.Count; pos++)
			{
				var col = _columns[pos];

				var val = value.Substring(col.Offset, col.Width);
				if (col.AllowTrim)
					val = col.AlignLeft ? val.TrimEnd() : val.TrimStart();

				rows[pos].Value = val;
			}

			return rows;
		}


		public override IEnumerable<SharpRow> GetRows(IEnumerable<object> values)
		{
			int pos = 0;
			foreach (var item in values)
			{
				if (pos >= _columns.Count)
					break;

				var column = _columns[pos++];
				if (column.Node != null)
					yield return new SharpValueRow(column.Node, item);
				
			}
			
			while (pos < _columns.Count)
			{
				var column = _columns[pos++];
				var node = column.Node;
				if (node != null)
				{
					var value = node.DefaultValue == null ? null : node.DefaultValue.Value;
					yield return new SharpValueRow(node, value);
				}
			}
		}


		public IEnumerable<SharpValue> GetRowValues(IEnumerable<object> values, bool useCache=true)
		{
			var rows = useCache && _cachedRow != null ? _cachedRow : GetColumnValues();

			int pos = 0;
			foreach (var item in values)
			{
				if (pos >= rows.Count)
					break;

				rows[pos++].Value = item;
			}

			while (pos < rows.Count)
			{
				rows[pos].Value = _columns[pos].DefaultValue.Value;
				pos++;
			}

			return rows;
		}

		protected List<SharpValue> GetColumnValues()
		{
			return
				_columns.Select(x => new SharpValue {Type = x.ValueType, Format = x.Format, Value = x.DefaultValue == null ? null : x.DefaultValue.Value})
					.ToList();
		}

		public static SharpValueRow CreateRow(List<SharpValueRow> rows, SharpNodeCollection nodes)
		{
			if (rows.Count() == 1)
				return rows[0];

			List<object> values = new List<object>();
			List<SharpNode> columns = new List<SharpNode>();

			StringBuilder sb = new StringBuilder();
			bool first = true;
			foreach (var row in rows)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append(row.Node.Index);
				if (row.Values == null)
					values.Add(null);
				else
					values.AddRange(row.Values);

				columns.Add(row.Node);
			}

			string path = sb.ToString();

			var node = nodes.GetNode(path);
			if (node == null)
			{
				node = new SharpNodeMap(columns);
				nodes.Add(node);
			}

			return new SharpValueRow(node, values);
		}

		public object GetColumnValue(int column, List<object> values)
		{
			var col = _columns[column];
			
			if (!(values[column] is string))
				return values[column];

			var val = values[column] as string;
			val = col.AlignLeft ? val.TrimEnd() : val.TrimStart();

			var colType = _columns[column].ValueType;

			if (colType == SharpValueType.None || colType == SharpValueType.String)
				return val;

			return SharpValue.ToValue(val, colType);
		}

		public object GetColumnValue(int column, string row)
		{
			var col = _columns[column];
			var val = row.Substring(col.Offset, col.Width);
			val = col.AlignLeft ? val.TrimEnd() : val.TrimStart();

			return val;
		}

		public void UpdateColumnValue(int column, object value, StringBuilder row)
		{
			if (column < Columns.Count)
			{
				var col = Columns[column];

				var val = SharpValue.ToString(value, col.ValueType);
				if (val.Length == col.Width)
				{
					row.Insert(col.Offset, val);
				}
				else if (val.Length > col.Width)
				{
					row.Insert(col.Offset,
						col.AlignLeft ? val.Substring(0, col.Width) : val.Substring(val.Length - col.Width, col.Width));
				}
				else
				{
					int padCount = col.Width - val.Length;
					if (col.AlignLeft)
					{
						row.Insert(col.Offset, val);
						row.Insert(col.Width + val.Length, col.Padding.ToString(), padCount);
					}
					else
					{
						row.Insert(col.Width, col.Padding.ToString(), padCount);
						row.Insert(col.Width + padCount, val);
					}
				}
			}
		}

		internal int GetColumnCount()
		{
			return _columns.Count;
		}
	}

	internal class SharpNodeMapColumn
	{
		public SharpNode Node;

		public int Index;
		public int Width;
		public int Offset;
		public SharpValueType ValueType = SharpValueType.None;

		public bool AlignLeft = true;
		public bool AllowTrim = true;
		public string Format = "";
		public string FieldName = "";
		public char Padding = ' ';

		public SharpValue DefaultValue = null;

		public SharpNodeMapColumn(SharpNode node, int width = -1, int offset = 0)
		{
			Node = node;
			Width = width;
			Offset = offset;
			Index = node.Index;
			ValueType = node.ValueType;
		}

		public SharpNodeMapColumn(int nodeIndex, SharpValueType valueType = SharpValueType.None,
			int width = -1, int offset = 0)
		{
			Index = nodeIndex;
			ValueType = valueType;
			Width = width;
			Offset = offset;
		}
	}
}
