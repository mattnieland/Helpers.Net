using Helpers.Net.Extensions;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class FlatWriter : IDisposable
	{
		private Stream _dest;
		private StreamWriter _writer = null;
		public bool IncludeHeader = true;
		public bool AllowQuotes = true;
		public bool AlwaysQuote = false;

		public FlatWriter(Stream dest, Encoding encoding = null)
		{
			_dest = dest;
			_writer = new StreamWriter(dest, encoding ?? Encoding.UTF8);
		}

		public void WriteDelimited(IEnumerable<SharpRow> rows, SharpMapType mapType = SharpMapType.None, char delimiter = ',', List<string> columns = null, Dictionary<string,string> aliases=null  )
		{
			columns = columns ?? new List<string>();

			if (mapType == SharpMapType.Variable && IncludeHeader)
			{
				var columnNames =
					columns.Select(x => aliases != null && aliases.ContainsKey(x) ? aliases[x] : x);
				
				var header = string.Join(delimiter.ToString(), AlwaysQuote ? columns.Select(x=>EncodedString.EncodeQuotedString(x)) : columns);

				_writer.WriteLine(header);
			}

			var includeNodes = new HashSet<string>(columns);

			foreach (var row in rows)
			{
				var nodeRow = row as SharpNodeRow;
				if (nodeRow  != null)
				{
					var next = nodeRow.First;
					var valRow = next as SharpValueRow;
					var rowValues = new Dictionary<string, string>();

					if (valRow != null && valRow.Node is SharpNodeMap)
					{
						var nodeMap = valRow.Node as SharpNodeMap;

						var columnValues = valRow.Values;

						if (nodeMap.MapType == SharpMapType.Fixed || nodeMap.MapType == SharpMapType.Variable)
						{
							if (valRow.Values.Count == 1)
							{
								columnValues = nodeMap.ExpandRow((string) valRow.Values[0]).Select(x => x.Value).ToList();
							}
						}

						for (int i = 0; i < nodeMap.Columns.Count && i < columnValues.Count;i++)
						{
							var col = nodeMap.Columns[i];
							if (includeNodes.Contains(col.Node.Name))
							{
								rowValues[col.Node.Name] = SharpValue.ToString(columnValues[i], col.ValueType);
							}
						}
					}
					else
					{
						while (next != null)
						{
							if (!includeNodes.Contains(next.Node.Name))
							{
								next = next.Next;
								continue;
							}

							valRow = next as SharpValueRow;
							var val = "";

							if (valRow != null && valRow.Node.IsSingleValueNode)
							{
								val = SharpValue.ToString(valRow.Values[0], valRow.Node.ValueType);
							}

							rowValues[next.Node.Name] = val;
					
							next = next.Next;
						}

					}
					
					StringBuilder line = new StringBuilder();
					bool first = true;

					foreach (var col in columns)
					{
						if (!first)
							line.Append(delimiter);
						first = false;

						if (rowValues.ContainsKey(col))
						{
							var val = rowValues[col];

							if (AlwaysQuote || (AllowQuotes && EncodedString.IsQuotedStringEncodingRequired(val, delimiter : delimiter, allowOnlyWhitespace : true)))
								val = EncodedString.EncodeQuotedString(val);

							line.Append(val);
						}
					}

					_writer.WriteLine(line);

				}
			}
		}

		public void Write(SharpNodeMap mapNode, IEnumerable<SharpRow> rows)
		{
			if (mapNode.MapType == SharpMapType.Variable && IncludeHeader)
			{
				var header = string.Join(mapNode.Delimiter.ToString(), mapNode.Columns.Select(x => x.FieldName));
				_writer.WriteLine(header);
			}

			var mapPath = mapNode.Parent.Path;

			foreach (var row in rows)
			{
				var valueRow = row as SharpValueRow;
				if (valueRow != null && valueRow.Node.Parent.Path == mapPath)
				{
					WriteFixedRow(valueRow, mapNode);
					continue;
				}

				var nodeRow = row as SharpNodeRow;
				if (nodeRow != null && nodeRow.Node.Path == mapPath)
				{
					WriteFixedRow(nodeRow, mapNode);
				}
			}
		}

		
		private void WriteLine(Dictionary<string, string> columns, SharpNodeMap mapNode)
		{
			StringBuilder line = new StringBuilder();

			foreach (var col in mapNode.Columns)
			{

				string val = "";

				if (columns.ContainsKey(col.Node.Name))
				{
					val = SharpValue.ToString(columns[col.Node.Name], col.Node.ValueType);
				}
				else
				{
					val = col.DefaultValue == null ? String.Empty : col.DefaultValue.ToString();
				}

				if (val.Length == col.Width)
				{
					line.Append(val);
				}
				else if (val.Length > col.Width)
				{
					line.Append(col.AlignLeft ? val.Substring(0, col.Width) : val.Substring(val.Length - col.Width, col.Width));
				}
				else
				{
					if (col.AlignLeft)
					{
						line.Append(val);
						line.Append(col.Padding, col.Width - val.Length);
					}
					else
					{
						line.Append(col.Padding, col.Width - val.Length);
						line.Append(val);
					}
				}
			}

			_writer.WriteLine(line.ToString());
		}


		private void WriteFixedRow(SharpValueRow row, SharpNodeMap mapNode)
		{
			var columns = new Dictionary<string, string>();

			var rowMap = row.Node as SharpNodeMap;
			
			var mapType = rowMap.MapType;

			List<SharpValue> rowValues = new List<SharpValue>();

			if (mapType == SharpMapType.Fixed)
			{
				rowValues = rowMap.ExpandRow((string) row.Values[0]).ToList();
			}
			else if (mapType == SharpMapType.Sequence)
			{
				rowValues = rowMap.GetRowValues(row.Values).ToList();
			}
			else if (mapType == SharpMapType.Variable)
			{
				if (row.Values.Count == 1 && row.Values[0] is string)
					rowValues = rowMap.ExpandRow((string) row.Values[0]).ToList();
				else
					rowValues = rowMap.GetRowValues((row.Values)).ToList();
			}

			int i = 0;
			
			foreach (var column in rowMap.Columns)
			{
				columns[column.Node.Name] = SharpValue.ToString(rowValues[i].Value, rowValues[i].Type);
				i++;
			}

			WriteLine(columns, mapNode);
		}

		private void WriteFixedRow(SharpNodeRow row, SharpNodeMap mapNode)
		{
			var columns = new Dictionary<string, string>();

			var next = row.First;
			if (next.Node.IsValueNode && !next.Node.IsSingleValueNode)
				if (next.Node.Parent.Path == mapNode.Parent.Path)
					return;

			while (next != null)
			{
				var vr = next as SharpValueRow;
				if (vr != null)
				{
					columns[next.Node.Name] = SharpValue.ToString(vr.Values[0], vr.Node.ValueType);
				}

				next = next.Next;
			}

			WriteLine(columns, mapNode);
		}

		private void WriteValueRow(SharpValueRow row, SharpMapType mapType = SharpMapType.None, char delimiter='\0')
		{
			if (row.Node.IsSingleValueNode)
				return;

			var mapNode = row.Node as SharpNodeMap;
			if (mapNode != null)
			{
				mapType = mapType == SharpMapType.None ? mapNode.MapType : mapType;
				delimiter = delimiter == '\0' ? mapNode.Delimiter : delimiter;
				if (mapType == SharpMapType.Fixed && row.Values.Count > 1)
					mapType = SharpMapType.Sequence;

				if (mapType== SharpMapType.Fixed)
				{
					_writer.WriteLine(row.Values[0]);
				}
				else if (mapType == SharpMapType.Variable)
				{
					if (row.Values.Count > 1)
					{
						StringBuilder line = new StringBuilder();
						bool first = true;
						int index = 0;
						foreach (var col in mapNode.Columns)
						{
							var val = (index < row.Values.Count
								? SharpValue.ToString(row.Values[index], col.ValueType)
								: col.DefaultValue.ToString()) ?? "";

							index++;
							if (!first) line.Append(delimiter);
							first = false;
							line.Append(val);
						}
						_writer.WriteLine(line);
					}
					else
					{
						_writer.WriteLine(row.Values[0]);	
					}
				}
				else if (mapType == SharpMapType.Sequence)
				{
					int index = 0;
					StringBuilder line = new StringBuilder();

					foreach (var col in mapNode.Columns)
					{
						var val = (index < row.Values.Count
							? SharpValue.ToString(row.Values[index], col.ValueType)
							: col.DefaultValue.ToString()) ?? "";

						index++;

						if (val.Length == col.Width)
						{
							line.Append(val);
						}
						else if (val.Length > col.Width)
						{
							line.Append(col.AlignLeft ? val.Substring(0, col.Width) : val.Substring(val.Length - col.Width, col.Width));
						}
						else
						{
							if (col.AlignLeft)
							{
								line.Append(val);
								line.Append(col.Padding, col.Width - val.Length);
							}
							else
							{
								line.Append(col.Padding, col.Width - val.Length);
								line.Append(val);
							}
						}
					}

					_writer.WriteLine(line.ToString());
				}
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
			if (_dest != null)
			{
				Flush();
				_writer = null;
				_dest = null;
			}
		}
	}
}
