using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
	internal class ConfigWriter : ISharpRowWriter
	{
		private Stream _dest;
		private readonly HashSet<int> _nodeSchemaHistory = new HashSet<int>();
		private SharpNode _lastParentNode = null;
		private string _lastPackage = "";
		private StreamWriter _writer = null;

		public ConfigWriter(Stream dest, Encoding encoding=null)
		{
			_dest = dest;
			_writer = new StreamWriter(dest, encoding ?? Encoding.UTF8);
		}

		public void Write(IEnumerable<SharpRow> rows)
		{
			foreach (var row in rows)
				WriteRow(row);
		}

		private void WriteRow(SharpRow row)
		{
			var nodeRow = row as SharpNodeRow;
			if (nodeRow != null)
			{
				if (nodeRow is SharpValueRow)
				{	
					WriteValueRow(nodeRow as SharpValueRow);
				}
				else
					WriteNodeRow(nodeRow);
				return;
			}

			var metaRow = row as SharpMetaRow;
			if (metaRow != null)
			{
				if (metaRow is SharpMetaCommentRow)
					_writer.WriteLine( metaRow.ToString() );
				else if (metaRow is SharpMetaParameterRow)
					WriteMetaParameterRow(metaRow as SharpMetaParameterRow);
				else if (metaRow is SharpMetaNodeRow)
					_writer.WriteLine( metaRow.ToString() );
			}
		}

		private string EncodeNodePath(SharpNode node, bool shortForm)
		{
			if (shortForm)
			{
				if (node.IsAttributeNode)
					return string.Format("  {0}", node.Name);
				else if (node.IsExpandedValueNode)
					return "  ";
				else
					return string.Format("  ./{0}", node.Name);
			}
			else
			{
				if (node.Path.EndsWith("/#"))
					return node.Path.Substring(0,node.Path.Length - 2);
				return node.Path;	
			}
		}

		private string EncodeNode(SharpNode node, bool shortForm = false, bool schemaOnly=false, bool includeIndex=false)
		{
			StringBuilder sb = new StringBuilder();

			if (_nodeSchemaHistory.Contains(node.Index))
			{
				sb.Append(EncodeNodePath(node, shortForm));
			}
			else
			{
				if (schemaOnly)
				{
					sb.Append("$");
					if (includeIndex)
					{
						sb.Append(node.Index);
					}
				
					sb.Append(" = ");
				}

				sb.Append(EncodeNodePath(node, shortForm));

				if (node.ValueType != SharpValueType.None || node.NodeType != SharpNodeType.Any)
				{
					sb.Append("~");

					if (node.NodeType != SharpNodeType.Any)
					{
						sb.Append(node.NodeType.ToString());
						if (node.ValueType != SharpValueType.None)
							sb.Append(" ");
					}

					if (node.ValueType != SharpValueType.None)
					{
						sb.Append(node.ValueType.ToString());
					}
				}

				if (!string.IsNullOrEmpty(node.Format))
				{
					sb.Append(string.Format("{{{0}}}", node.Format));
				}

				if (schemaOnly && node.DefaultValue != null && node.DefaultValue.Value != null)
				{
					sb.Append(string.Format(" := {0}", node.DefaultValue.Value));
				}

				_nodeSchemaHistory.Add(node.Index);
			}

			return sb.ToString();
		}

		private void WriteNodeRow(SharpNodeRow row)
		{
			if (row.Node.PackageName.ToLower() != _lastPackage)
				WritePackage(row.Node.PackageName);

			_writer.WriteLine( EncodeNode(row.Node) );
			_lastParentNode = row.Node;
		}

		private void WriteValueRow(SharpValueRow row)
		{
			if (row.Node.IsSingleValueNode)
			{
				if (row.Node.PackageName.ToLower() != _lastPackage)
					WritePackage(row.Node.PackageName);

				if (row.Node.DefaultValue != null && !_nodeSchemaHistory.Contains(row.Node.Index))
					_writer.WriteLine( EncodeNode(row.Node, schemaOnly: true));

				StringBuilder sb = new StringBuilder();
				var ns = EncodeNode(row.Node, shortForm: row.Node.Parent == _lastParentNode);
				sb.Append(ns);
				if (string.IsNullOrWhiteSpace(ns))
					sb.Append("= ");
				else
					sb.Append(" = ");
				
				sb.Append(SharpValue.EncodeValue(row.Values[0], row.Node.ValueType));
				_writer.WriteLine(sb.ToString());
			}
			else
			{
				IEnumerable<object> values = null;

				var mapNode = row.Node as SharpNodeMap;
				if (mapNode != null && row.Values.Count == 1)
				{
					if (mapNode.MapType == SharpMapType.Fixed || mapNode.MapType == SharpMapType.Variable)
					{
						values = mapNode.ExpandRow(row.Values[0].ToString()).Select(x => x.Value);
					}
				}


				foreach (var innerRow in row.Node.GetRows(values ?? row.Values))
				{
					WriteRow(innerRow);
				}
			}
		}

		private void WriteMetaParameterRow(SharpMetaParameterRow row)
		{
			if (string.Compare(row.Key, "package", StringComparison.InvariantCultureIgnoreCase) == 0)
			{
				if (_lastPackage == row.Value.ToString())
					return;

				_lastPackage = row.Value.ToString();
			}

			_writer.WriteLine(row.ToString());
		}

		private void WritePackage(string package)
		{
			if (_lastPackage == package.ToLower())
				return;

			_writer.WriteLine("#! package = {0}", package);
			_lastPackage = package;
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
