using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal interface ISharpRowReader : IEnumerable<SharpRow>, IDisposable
    {
    }

    internal interface ISharpRowWriter : IDisposable
    {
        void Write(IEnumerable<SharpRow> rows);
    }

    internal delegate ISharpRowReader SharpRowReader(Stream stream, SharpNodeCollection nodes);

    internal delegate ISharpRowReader SharpRowReaderWithEncoding(Stream stream, SharpNodeCollection nodes, Encoding encoding);

    internal delegate ISharpRowWriter SharpRowWriter(Stream stream, SharpNodeCollection nodes, Encoding encoding = null);

    public class SharpFile : IDisposable
    {
        public SharpObject Context = new SharpObject();

        private readonly SharpNodeCollection _nodes = new SharpNodeCollection();
        private SharpRowCollection _rows = new SharpRowCollection();
        private readonly SharpConstantCollection _constants = new SharpConstantCollection();
        private readonly Encoding _defaultEncoding = new UTF8Encoding(false);
        private readonly Dictionary<string, SharpNodeMap> _mapSets = new Dictionary<string, SharpNodeMap>();
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();

        private Dictionary<string, string> _aliases = new Dictionary<string, string>();
        private List<string> _columns = new List<string>();
        private char _delimiter = ',';
        private bool _allowQuotes = true;
        private bool _alwaysQuote = false;

        private FileStream _stream;

        private string _defaultHeaderLine;
        private bool _skipReadHeader = false;
        private bool _skipEmptyRows = false;

        public SharpFile()
        {
            _rows.Nodes = _nodes;
        }

        public string GetProperty(string key, string defaultValue = null)
        {
            return _properties.ContainsKey(key) ? _properties[key] : defaultValue;
        }

        public Dictionary<string, string> Properties
        {
            get { return _properties; }
        }

        public Dictionary<string, string> Aliases
        {
            get { return _aliases; }
            set { _aliases = value; }
        }

        public List<string> Columns
        {
            get { return _columns; }
            set { _columns = value; }
        }

        public char Delimiter
        {
            get { return _delimiter; }
            set { _delimiter = value; }
        }

        public bool AllowQuotes
        {
            get { return _allowQuotes; }
            set { _allowQuotes = value; }
        }

        public bool AlwaysQuote
        {
            get { return _alwaysQuote; }
            set { _alwaysQuote = value; }
        }

        public string DefaultHeaderLine
        {
            get { return _defaultHeaderLine; }
            set { _defaultHeaderLine = value; }
        }

        public bool SkipReadHeader
        {
            get { return _skipReadHeader; }
            set { _skipReadHeader = value; }
        }

        public bool SkipEmptyRows
        {
            get { return _skipEmptyRows; }
            set { _skipEmptyRows = value; }
        }

        public List<SharpObject> GetColumnMapping(string path)
        {
            SharpNodeMap result;
            if (!_mapSets.TryGetValue(path, out result))
                return new List<SharpObject>();

            return result.Columns.Select(x => SharpObject.Copy(new
            {
                x.FieldName,
                x.Width,
                x.Offset,
            })).ToList();
        }

        public void Clear()
        {
            _nodes.Clear();
            _rows.Clear();
            _constants.Clear();
        }

        public void Dispose()
        {
            Close();
        }

        public void Close()
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }
        }

        public string GetRootPath()
        {
            var rootNode = _nodes.GetNode(0);
            if (rootNode != null)
                return rootNode.Path;
            return "";
        }

        #region

        public void InsertAllOnSubmit(string nodePath, IEnumerable<ISharpObject> items)
        {
            _rows.AddRange(nodePath, items, true);
        }

        public SharpObject InsertOnSubmit(string nodePath, IDictionary<string, object> item)
        {
            var source = _rows.Add(nodePath, item, true);
            return new SharpObject(source);
        }

        public void DeleteOnSubmit(SharpObject item)
        {
            _rows.Remove(item.GetRow());
        }

        public void SubmitChanges()
        {
            _rows.SubmitChanges();
        }

        public void DiscardPendingChanges()
        {
            _rows.DiscardPendingChanges();
        }

        #endregion

        #region Create SharpObjects

        public SharpObject SelectRoot()
        {
            var source = _rows.GetRoot();            
            return new SharpObject(source);                        
        }

        public IEnumerable<SharpObject> SelectAll()
        {
            return _rows.GetTopRows().Select(row => new SharpObject(row as SharpNodeRow));
        }

        public IEnumerable<SharpObject> Select(string query)
        {
            var node = _nodes.GetNode(query);
            if (node != null)
            {
                foreach (var row in _rows.GetRows(node: node))
                {
                    yield return new SharpObject(row as SharpNodeRow);
                }
            }
        }

        public IEnumerable<T> Select<T>(string query, Func<SharpObject, T> init)
        {
            var node = _nodes.GetNode(query);
            if (node != null)
            {
                foreach (var row in _rows.GetRows(node: node))
                {
                    yield return init(new SharpObject(row as SharpNodeRow));
                }
            }
        }

        #endregion


        #region Private File Read/Write Methods

        private void OpenFile(string filename, string nodePath, SharpRowReader rowReader)
        {
            if (!string.IsNullOrEmpty(nodePath))
            {
                var node = _nodes.GetNode(nodePath);
                if (node == null)
                {
                    _nodes.Add(new SharpNode
                    {
                        Path = nodePath,
                        Name = SharpNode.GetNodeName(nodePath)
                    });
                }
            }

            _stream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            _rows = new SharpRowStream
            {
                Nodes = _nodes,
                StreamNode = string.IsNullOrEmpty(nodePath) ? null : _nodes.GetNode(nodePath),
                StreamSource = rowReader(_stream, _nodes)
            };
        }

        private void LoadStream(Stream source, SharpRowReader rowReader)
        {
            using (var reader = rowReader(source, _nodes))
            {
                var rows = reader.ToList();
                _rows.AddRange(rows, insertParentNodes: true, removeComments: true);
                _rows.SubmitChanges();
            }
        }

        private void LoadFile(string filename, SharpRowReader rowReader)
        {
            using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                using (var reader = rowReader(source, _nodes))
                {
                    var rows = reader.ToList();
                    _rows.AddRange(rows, insertParentNodes: true, removeComments: true);
                    _rows.SubmitChanges();
                }
            }
        }

        private void LoadFile(string filename, SharpRowReaderWithEncoding rowReader, Encoding encoding)
        {
            using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                using (var reader = rowReader(source, _nodes, encoding))
                {
                    var rows = reader.ToList();
                    _rows.AddRange(rows, insertParentNodes: true, removeComments: true);
                    _rows.SubmitChanges();
                }
            }
        }

        private void LoadString(string content, SharpRowReader rowReader)
        {
            using (var source = new MemoryStream(_defaultEncoding.GetBytes(content)))
            {
                using (var reader = rowReader(source, _nodes))
                {
                    _rows.AddRange(reader, insertParentNodes: true, removeComments: true);
                    _rows.SubmitChanges();
                }
            }
        }

        private void SaveFile(string filename, SharpRowWriter rowWriter, Encoding encoding = null)
        {
            Helpers.Net.Extensions.IO.TryCreateDirectory(Path.GetDirectoryName(filename));

            using (var dest = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
            {
                using (var writer = rowWriter(dest, _nodes, encoding ?? _defaultEncoding))
                {
                    writer.Write(_rows.GetRows());
                }
            }
        }

        private string SaveString(SharpRowWriter rowWriter, Encoding encoding = null)
        {
            using (var dest = new MemoryStream())
            {
                var enc = encoding ?? _defaultEncoding;
                using (var writer = rowWriter(dest, _nodes, enc))
                {
                    writer.Write(_rows.GetRows());
                }

                return enc.GetString(dest.GetBuffer());
            }
        }

        #endregion


        #region Flat Files

        public void SaveFlatFile(string rootNodePath, string filename, string formatfile = null, Encoding encoding = null)
        {
            if (formatfile == null)
            {
                var fi = new FileInfo(filename);
                formatfile = filename.Substring(0, filename.Length - fi.Extension.Length) + ".fmt";
            }

            var rows = _rows.GetRows();

            SharpNodeMap nodeMap;
            if (!_mapSets.TryGetValue(rootNodePath, out nodeMap))
            {
                var rootNode = _nodes.GetNode(rootNodePath);
                var maps = _nodes.GetNodes().Where(n => n is SharpNodeMap && n.Parent == rootNode).ToList();
                if (maps.Count > 1)
                    throw new Exception(string.Format("Could not save flat file format: Multiple column mappings found for '{0}'", rootNodePath));

                nodeMap = maps.FirstOrDefault() as SharpNodeMap;
            }

            if (nodeMap == null)
                throw new Exception(string.Format("Could not save flat file format: No column mapping not found for '{0}'", rootNodePath));

            FormatWriter.SaveFormatFile(formatfile, nodeMap, encoding ?? _defaultEncoding);

            using (var dest = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
            {
                using (var writer = new FlatWriter(dest, encoding ?? _defaultEncoding))
                {
                    writer.Write(nodeMap, rows);
                }
            }
        }

        public void LoadFormatFile(string rootNodePath, string formatfile, Encoding encoding = null)
        {
            var mapNode = FormatReader.LoadFormatFile(formatfile, _nodes, rootNodePath, encoding ?? _defaultEncoding, _aliases);
            _mapSets[rootNodePath] = mapNode;
        }

        public void LoadFlatFile(string rootNodePath, string filename, string formatfile = null, Encoding encoding = null)
        {
            if (formatfile == null)
            {
                var fi = new FileInfo(filename);
                formatfile = filename.Substring(0, filename.Length - fi.Extension.Length) + ".fmt";
            }

            var mapNode = FormatReader.LoadFormatFile(formatfile, _nodes, rootNodePath, encoding ?? _defaultEncoding, _aliases);

            _mapSets[rootNodePath] = mapNode;

            using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                // Create node rows for parents of map node
                var nodes = new List<SharpNode> { mapNode.Parent };
                while (nodes[0].Parent != null)
                    nodes.Insert(0, nodes[0].Parent);

                nodes.Remove(nodes.Last());
                var rows = nodes.Select(n => new SharpNodeRow(n)).ToList();
                _rows.AddRange(rows);

                using (var reader = new FlatReader(source, mapNode, encoding ?? _defaultEncoding, SkipEmptyRows))
                {
                    _rows.InsertRows(reader.GetEnumerator(), rootRow: rows.Last());
                }

                _rows.SubmitChanges();
            }
        }

        #endregion

        #region Delimited Files

        public void SaveDelimitedFile(string rootNodePath, string filename, Encoding encoding = null, bool includeHeader = true)
        {
            using (var dest = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
            {
                using (var writer = new FlatWriter(dest, encoding ?? _defaultEncoding))
                {
                    writer.AllowQuotes = _allowQuotes;
                    writer.AlwaysQuote = _alwaysQuote;
                    writer.IncludeHeader = includeHeader;

                    var rootNode = _nodes.GetNode(rootNodePath);
                    if (_columns.Count == 0)
                    {
                        _columns = rootNode.Members.Where(x => x.Value.IsSingleValueNode).Select(x => x.Value.Name).ToList();
                    }

                    var rows = _rows.GetRows(rootNode).ToList();

                    writer.WriteDelimited(rows, SharpMapType.Variable, _delimiter, _columns, _aliases);
                }
            }
        }

        public void LoadDelimitedFile(string rootNodePath, string filename, Encoding encoding = null)
        {
            using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                if (!SkipReadHeader)
                {
                    var headerReader = new StreamReader(source);
                    DefaultHeaderLine = headerReader.ReadLine();
                }

                var mapNode = FormatReader.LoadHeaderLine(DefaultHeaderLine, _delimiter, _nodes, rootNodePath, _aliases);
                _mapSets[rootNodePath] = mapNode;

                // Create node rows for parents of map node
                var nodes = new List<SharpNode> { mapNode.Parent };
                while (nodes[0].Parent != null)
                    nodes.Insert(0, nodes[0].Parent);

                nodes.Remove(nodes.Last());
                var rows = nodes.Select(n => new SharpNodeRow(n)).ToList();
                _rows.AddRange(rows);

                source.Seek(0, SeekOrigin.Begin);
                using (var reader = new FlatReader(source, mapNode, encoding, SkipEmptyRows))
                {
                    reader.SkipLines = SkipReadHeader ? 0 : 1;
                    _rows.InsertRows(reader.GetEnumerator(), rootRow: rows.Last());
                }

                _rows.SubmitChanges();
            }
        }

        public void OpenDelimitedFile(string rootNodePath, string filename, Encoding encoding = null)
        {
            using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                if (!SkipReadHeader)
                {
                    var headerReader = new StreamReader(source);
                    DefaultHeaderLine = headerReader.ReadLine();
                }

                var mapNode = FormatReader.LoadHeaderLine(DefaultHeaderLine, _delimiter, _nodes, rootNodePath, _aliases);
                _mapSets[rootNodePath] = mapNode;

                _stream = new FileStream(filename, FileMode.Open, FileAccess.Read);
                _rows = new SharpRowStream
                {
                    Nodes = _nodes,
                    StreamNode = _nodes.GetNode(rootNodePath),
                    StreamSource = new FlatReader(_stream, mapNode, encoding, SkipEmptyRows)
                };

                // Create node rows for parents of map node
                var nodes = new List<SharpNode> { mapNode.Parent };
                while (nodes[0].Parent != null)
                    nodes.Insert(0, nodes[0].Parent);

                nodes.Remove(nodes.Last());

                var rows = nodes.Select(n => new SharpNodeRow(n)).ToList();
                _rows.AddRange(rows);
            }
        }

        #endregion        

        #region Xml Files

        public void OpenXml(string filename, string nodePath = "")
        {
            OpenFile(filename, nodePath, (s, n) => new XmlReader(s, n));
        }

        public void OpenConfig(string filename, string nodePath = "")
        {
            OpenFile(filename, nodePath, (s, n) => new ConfigReader(s, n));
        }

        public void LoadXml(string filename)
        {
            LoadFile(filename, (s, n) => new XmlReader(s, n));
        }

        public void LoadXml(string filename, Encoding encoding)
        {
            LoadFile(filename, (s, n, e) => new XmlReader(s, n, e), encoding);
        }

        public void LoadXmlString(string content)
        {
            LoadString(content, (s, n) => new XmlReader(s, n));
        }

        public void SaveXml(string filename, Encoding encoding = null)
        {
            SaveFile(filename, (s, n, e) => new XmlWriter(s, e), encoding);
        }

        public string SaveXmlString(Encoding encoding = null)
        {
            return SaveString((s, n, e) => new XmlWriter(s, e), encoding);
        }

        #endregion

        #region Config Files

        public void LoadConfigString(string content)
        {
            LoadString(content, (s, n) => new ConfigReader(s, n, _defaultEncoding));
        }

        public void LoadConfig(Stream source, Encoding encoding = null)
        {
            LoadStream(source, (s, n) => new ConfigReader(s, n, encoding ?? _defaultEncoding));
        }

        public void LoadConfig(string filename, Encoding encoding = null)
        {
            LoadFile(filename, (s, n) => new ConfigReader(s, n, encoding ?? _defaultEncoding));
        }

        public void SaveConfig(string filename, Encoding encoding = null)
        {
            SaveFile(filename, (s, n, e) => new ConfigWriter(s, e), encoding);
        }

        public string SaveConfigStream(Encoding encoding = null)
        {
            return SaveString((s, n, e) => new ConfigWriter(s, e), encoding);
        }

        #endregion

        #region Schema Files
        public void SaveSchema(string filename, Encoding encoding = null)
        {
            using (var dest = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
            {
                using (var writer = new ConfigWriter(dest, encoding ?? _defaultEncoding))
                {
                    var schemaRows = _nodes.GetRows().Where(r => !(r is SharpMetaNodeMapRow));
                    writer.Write(schemaRows);
                }
            }
        }

        public IEnumerable<SharpNodeSchema> EnumerateSchema()
        {
            foreach (var node in _nodes.GetNodes())
            {
                if (node is SharpNodeMap) continue;

                yield return node.NodeSchema;
            }
        }

        public void LoadSchema(string filename, Encoding encoding = null)
        {
            using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                using (var reader = new ConfigReader(source, _nodes, encoding ?? _defaultEncoding))
                {
                    foreach (var row in reader)
                    {
                        var parameterRow = row as SharpMetaParameterRow;

                        if (parameterRow != null)
                            _properties[parameterRow.Key] = parameterRow.Value.ToString();
                    }
                }
            }

            _nodes.NextIndex = _nodes.GetNodeIds().Max() + 1;
        }

        #endregion
    }
}
