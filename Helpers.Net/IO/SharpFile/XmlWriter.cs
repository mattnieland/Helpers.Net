using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace Helpers.Net.IO.SharpFile
{
    internal class XmlWriter : ISharpRowWriter
	{
		private Stream _dest;
		private string _lastPackage = "";
		private System.Xml.XmlWriter _writer;

		private readonly SharpNodeStack _nodeStack = new SharpNodeStack();
		private readonly List<SharpMetaCommentRow> _commentBlock = new List<SharpMetaCommentRow>();
		private Encoding _encoding;

		public XmlWriter(Stream dest, Encoding encoding = null)
		{
			_dest = dest;
			_encoding = encoding ?? Encoding.UTF8;
		}

		public void Write(IEnumerable<SharpRow> rows)
		{
			var settings = new System.Xml.XmlWriterSettings
			{
				Indent = true,
				Encoding = _encoding
			};

			_writer = System.Xml.XmlWriter.Create(_dest, settings);

			_writer.WriteStartDocument();
			foreach (var row in rows)
				WriteRow(row);

			if (_commentBlock.Count > 0)
				WriteCommentBlock();

			while (_nodeStack.Count > 0)
			{
				_writer.WriteEndElement();
				_nodeStack.Dequeue();
			}

			_writer.WriteEndDocument();
			_writer.Flush();
			_writer = null;
		}

		private void WriteCommentBlock()
		{
			StringBuilder sb = new StringBuilder();
			foreach (var commentRow in _commentBlock)
			{
				sb.AppendLine(commentRow.Value);
			}
			_writer.WriteComment(sb.ToString());
			_commentBlock.Clear();
		}
		private void WriteRow(SharpRow row)
		{
			if (!(row is SharpMetaCommentRow) && _commentBlock.Count > 0)
			{
				WriteCommentBlock();
			}


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
					_commentBlock.Add(metaRow as SharpMetaCommentRow);
				else if (metaRow is SharpMetaParameterRow)
					WriteMetaParameterRow(metaRow as SharpMetaParameterRow);
			}
		}

		
		private void WriteNodeRow(SharpNodeRow row)
		{
			if (row.Node.PackageName.ToLower() != _lastPackage)
				WritePackage(row.Node.PackageName);

			while (!_nodeStack.IsEmpty && _nodeStack.Top != row.Node && !_nodeStack.Top.HasDescendant(row.Node))
			{
				_nodeStack.Dequeue();
				_writer.WriteEndElement();
			}

			if (_nodeStack.Top == row.Node)
			{
				_writer.WriteEndElement();
				_writer.WriteStartElement(row.Node.Name);
			}
			else
			{
				if (!_nodeStack.IsEmpty && _nodeStack.Top != row.Node.Parent)
					WriteNodeRow(new SharpNodeRow(row.Node.Parent));

				_nodeStack.Enqueue(row.Node);
				_writer.WriteStartElement(row.Node.Name);		
			}
		}

		private void WriteValueRow(SharpValueRow row)
		{
			if (row.Node.IsSingleValueNode)
			{
				if (row.Node.PackageName.ToLower() != _lastPackage)
					WritePackage(row.Node.PackageName);

				while (!_nodeStack.IsEmpty && _nodeStack.Top != row.Node.Parent)
				{
					_nodeStack.Dequeue();
					_writer.WriteEndElement();
				}

				if (_nodeStack.IsEmpty)
					WriteNodeRow(new SharpNodeRow(row.Node.Parent));

				if (row.Node.IsAttributeNode)
				{
					_writer.WriteAttributeString(row.Node.Name.TrimStart('@'), row.Values[0].ToString());
				}
				else if (row.Node.IsExpandedValueNode)
				{
					_writer.WriteValue(row.Values[0] ?? "");
				}
				else
				{
					var val = row.Values[0] == null ? "" : row.Values[0].ToString();
					_writer.WriteElementString(row.Node.Name, val);
				}
			}
			else
			{
				foreach (var innerRow in row.Node.GetRows(row.Values))
				{
					WriteRow(innerRow);
				}
			}
		}

		private void WriteMetaParameterRow(SharpMetaParameterRow row)
		{
			if (string.Compare(row.Key, "XmlDeclaration", StringComparison.InvariantCultureIgnoreCase) == 0)
				return;
		
			if (string.Compare(row.Key, "package", StringComparison.InvariantCultureIgnoreCase) == 0)
			{
				if (_lastPackage == row.Value.ToString())
					return;

				_lastPackage = row.Value.ToString();
			}

			_writer.WriteProcessingInstruction(row.Key, row.Value.ToString());
		}

		private void WritePackage(string package)
		{
			if (_lastPackage == package.ToLower())
				return;

			_writer.WriteProcessingInstruction("package", package);
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
				if (_writer != null)
					_writer.WriteEndDocument();

				Flush();
				_writer = null;
				_dest = null;
			}
		}
	}
}
