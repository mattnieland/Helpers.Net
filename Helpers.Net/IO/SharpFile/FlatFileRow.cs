using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
	public class FlatFileRow
    {
        #region Private Properties

        private int _version;
		private int _rowSpan;
		private string _emptyRow = "";
		private Encoding _encoding = Encoding.ASCII;

        #endregion

        #region Protected Properties

        protected StringBuilder RowData = new StringBuilder();
        protected int Column = 0;

        #endregion

        #region Public properties

        public List<int> ColumnOffsets = new List<int>();
		public List<int> ColumnWidths = new List<int>();
		public List<string> ColumnFormat = new List<string>();
		public Dictionary<string, int> ColumnIndex = new Dictionary<string, int>();
		public Dictionary<int, string> ColumnNames = new Dictionary<int, string>();
		public Dictionary<string, string> ColumnAliases = new Dictionary<string, string>();
		public Dictionary<string, string> ColumnTypes = new Dictionary<string, string>();
		public bool AutoTypeColumns = false;
		public bool AutoTrimColumns = true;
		public bool IncludeEmptyFields = false;
		public bool HasInitialized = false;
        public bool AllowNestedPaths = true;

		public int RowSpan
		{
			get { return _rowSpan; }
			set
			{
				_rowSpan = value;
				StringBuilder sb = new StringBuilder();
				sb.Append(' ', _rowSpan);
				_emptyRow = sb.ToString();
			}
		}

		public int Version
		{
			get { return _version; }
			set { _version = value; }
		}

		public Encoding Encoding
		{
			get { return _encoding; }
			set { _encoding = value; }
		}

        #endregion

        #region Public Methods

        public string GetFieldValue(string fieldName, string source, int colSpan = 0)
        {
            int pos;
            if (!ColumnIndex.TryGetValue(fieldName, out pos))
                return string.Empty;

            var startpos = ColumnOffsets[pos];
            var width = ColumnWidths[pos++];
            while (colSpan > 0)
            {
                width += ColumnWidths[pos++];
                colSpan--;
            }
            return source.Substring(startpos, width);
        }

        public void LoadFormatFile(string filename, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.ASCII;
            ColumnFormat = File.ReadAllLines(filename, encoding).ToList();
            InitializeColumns();
        }

        public void SaveFormatFile(string filename, Encoding encoding = null)
        {
            if (!Directory.Exists(Path.GetDirectoryName(filename)))
                Helpers.Net.Extensions.IO.TryCreateDirectory(Path.GetDirectoryName(filename));

            encoding = encoding ?? Encoding.ASCII;
            if (ColumnFormat.Count == 0)
                LoadDefaultColumns();


            using (var writer = new StreamWriter(filename, false, encoding))
            {
                foreach (var line in ColumnFormat)
                    writer.WriteLine(line);

                if (ColumnFormat.Count == 0 || !ColumnFormat.Last().StartsWith("EOR,"))
                    writer.WriteLine("EOR,2,b");
            }
        }

        public bool TrySaveFormatFile(string filename, Encoding encoding = null)
        {
            try
            {
                SaveFormatFile(filename, encoding);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Public Virtual Methods

        public virtual string GetColumnName(string field)
        {
            string result;
            if (!ColumnAliases.TryGetValue(field, out result))
                result = field;
            return result;
        }

        public virtual object GetColumnValue(string field, string value)
        {
            string type;
            if (AutoTrimColumns)
                value = value.Trim();

            if (ColumnTypes.TryGetValue(field, out type))
            {
                SharpValueType vt;
                if (Enum.TryParse(type, out vt))
                    return SharpValue.ToValue(value, vt);
            }

            return AutoTypeColumns ? SharpValue.ToValue(value, SharpValueType.Auto) : value;
        }

        public virtual string[] GetColumns(string source)
        {
            var result = new string[ColumnWidths.Count];
          
            for (var i = 0; i < ColumnWidths.Count; i++)
            {
                var offset = ColumnOffsets[i];
                var width = ColumnWidths[i];
                if (offset < source.Length)
                {
                    result[i] = source.Substring(offset, width);
                }
                else
                {
                    result[i] = "";
                }
            }

            return result;
        }

        public virtual SharpObject GetObject(string source)
        {
            // TODO: Add option to use packed+collapsed rows for faster reading when only a few columns are needed

            if (ColumnWidths.Count == 0) return new SharpObject();

            var column = 0;
            var width = ColumnWidths[column];
            var sb = new StringBuilder();
            var result = new SharpObject();

            foreach (var c in source)
            {
                sb.Append(c);
                width--;
                if (width == 0)
                {
                    var field = GetColumnName(ColumnNames[column]);
                    var value = GetColumnValue(ColumnNames[column], sb.ToString());

                    if (IncludeEmptyFields || (value != null && (!(value is string) || !string.IsNullOrEmpty(value.ToString()))))
                    {
                        var parts = field.Split('/').ToList();
                        StringBuilder path = new StringBuilder();
                        for (int i = 0; i < parts.Count - 1; i++)
                        {
                            if (path.Length > 0) path.Append("/");
                            path.Append(parts[i]);

                            var p = path.ToString();
                            if (!result.ContainsKey(p))
                                result[p] = new SharpObject();
                        }


                        if (result.ContainsKey(field))
                            result.Add(field, value);
                        else
                            result[field] = value;
                    }

                    column++;
                    if (column >= ColumnWidths.Count) break;
                    width = ColumnWidths[column];
                    sb.Clear();
                }
            }

            return result;
        }

        public virtual Dictionary<string, string> GetFlatObject(string source)
        {
            if (ColumnWidths.Count == 0) return new Dictionary<string, string>();

            var column = 0;
            var width = ColumnWidths[column];
            var sb = new StringBuilder();
            var result = new Dictionary<string, string>();

            foreach (var c in source)
            {
                sb.Append(c);
                width--;
                if (width == 0)
                {
                    var field = GetColumnName(ColumnNames[column]);
                    var value = sb.ToString();

                    if (AutoTrimColumns)
                        value = value.Trim();

                    result.Add(field, value);

                    column++;
                    if (column >= ColumnWidths.Count) break;
                    width = ColumnWidths[column];
                    sb.Clear();
                }
            }

            return result;
        }

        public virtual string GetRowData(SharpObject source)
        {
            var reverseAliases = new Dictionary<string, string>();
            foreach (var alias in ColumnAliases)
                reverseAliases[alias.Value] = alias.Key;

            RowData.Clear();
            Column = 0;
            foreach (var column in ColumnNames)
            {
                string colname;
                if (!reverseAliases.TryGetValue(column.Value, out colname))
                    colname = column.Value;

                if (!source.ContainsKey(colname))
                {
                    AppendEmptyColumn(1);
                }
                else
                {
                    // TODO: apply formatting, padding, alignment etc. here
                    var value = (source[colname] ?? "").ToString();
                    AppendJustifyLeft(value);
                }
            }

            return RowData.ToString();
        }

        public virtual void InitializeColumns()
        {
            if (ColumnFormat.Count == 0)
                LoadDefaultColumns();

            ColumnWidths.Clear();
            ColumnOffsets.Clear();
            ColumnNames.Clear();
            ColumnWidths.Clear();

            int offset = 0;
            int index = 0;
            int lineno = 0;
            foreach (var line in ColumnFormat)
            {
                lineno++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (parts.Count != 3)
                    throw new FormatException(string.Format("Wrong number of columns in format file (Line: {0})", lineno));

                string columnName = parts[0];
                int columnWidth;
                if (!int.TryParse(parts[1], out columnWidth))
                    throw new FormatException(string.Format("Cannot parse column width in format file (Line: {0}, Column: {1})", lineno, columnName));

                ColumnOffsets.Add(offset);
                ColumnWidths.Add(columnWidth);
                ColumnNames[index] = columnName;
                ColumnIndex[columnName] = index++;

                offset += columnWidth;
            }

            ColumnOffsets.Add(offset);
            ColumnWidths.Add(0);

            RowSpan = offset;
            if (ColumnNames.Count > 0 && ColumnNames.Last().Value == "EOR")
                RowSpan -= 2;

            LoadColumnAliases();
            LoadColumnTypes();
        }

        public virtual void LoadColumnAliases()
		{
			
		}

		public virtual void LoadColumnTypes()
		{
			
		}

        public virtual bool ReplaceField(string fieldName, string value, byte[] buffer, int offset = 0)
        {

            int index;
            if (!ColumnIndex.TryGetValue(fieldName, out index))
                return false;

            var pos = ColumnOffsets[index];
            var width = ColumnWidths[index];

            if (value.Length < width)
            {
                var sb = new StringBuilder(value);
                sb.Append(' ', width - value.Length);
                value = sb.ToString();
            }

            var valueBuff = _encoding.GetBytes(value);
            Buffer.BlockCopy(valueBuff, 0, buffer, pos + offset, valueBuff.Length);
            return true;
        }

        #endregion

        #region Protected Virtual Methods

        protected virtual void AppendEmptyColumn(int offset, char padding = ' ')
        {
            int pos = ColumnOffsets[Column];
            Column += offset;
            int nextPos = ColumnOffsets[Column];
            RowData.Append(padding, nextPos - pos);
        }

        protected virtual void AppendJustifyLeft(string value, char padding = ' ')
        {
            value = value ?? String.Empty;
            int pos = ColumnOffsets[Column++];
            int nextPos = ColumnOffsets[Column];
            pos += value.Length;
            if (pos > nextPos)
            {
                RowData.Append(value.Substring(0, value.Length - (pos - nextPos)));
            }
            else
            {
                RowData.Append(value);
                if (pos != nextPos)
                    RowData.Append(padding, nextPos - pos);
            }

        }

        protected virtual void AppendJustifyRight(string value, char padding = ' ')
        {
            value = value ?? String.Empty;
            int pos = ColumnOffsets[Column++];
            int nextPos = ColumnOffsets[Column];
            pos += value.Length;
            if (pos > nextPos)
            {
                RowData.Append(value.Substring(0, value.Length - (pos - nextPos)));
            }
            else
            {
                if (pos != nextPos)
                    RowData.Append(padding, nextPos - pos);
                RowData.Append(value);
            }
        }

        protected virtual void AppendSpanColumn(string value, int offset, char padding = ' ')
        {
            value = value ?? String.Empty;
            int pos = ColumnOffsets[Column];
            Column += offset;
            int nextPos = ColumnOffsets[Column];
            pos += value.Length;
            if (pos > nextPos)
            {
                if (value.Length < (pos - nextPos))
                {
                    RowData.Append(value);
                    RowData.Append(padding, (pos - nextPos) - value.Length);
                }
                else
                {
                    RowData.Append(value.Substring(0, value.Length - (pos - nextPos)));
                }
            }
            else
            {
                RowData.Append(value);
                if (pos != nextPos)
                    RowData.Append(padding, nextPos - pos);
            }
        }

        protected virtual void LoadDefaultColumns()
        {

        }

        #endregion
	}
}
