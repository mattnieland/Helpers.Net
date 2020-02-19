using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Helpers.Net.Extensions;
using TimeZoneConverter;

namespace Helpers.Net.Loggers
{
	#region Log Level & Message
	public enum LogLevel
	{
		None,               // used as a filter level to exclude all messages
		Critical,           // indicates an event that should never happen
		Fatal,              // indicates an event that will result in the application aborting execution
		Error,              // indicates an event that should not happen
		Warning,            // indicates an event that is potentially problematic, abnormal or unexpected
		Info,               // indicates progress of application at a coarse grained level
		Message,            // indicates progress of application at a fine grained level
		Debug,              // debug level fine grained information 
		Monitor,            // messages associated with progress of a long running process
		Assert,             // messages only process when the asserted condition is false
		Event,              // message used by event system
		Link,               // message used to associate metadata to a file or other resource
		Result,             // message used to log task results 
		All                 // used as a filter level to include all messages
	}
	public class LogMessage
	{
		public string MessageId = Extensions.EncodedString.GuidToString(Guid.NewGuid());

		public LogLevel LogLevel;
		public string Message = "";

		// Messages are associated with a resource path
		public string SourcePath;
		public string SourceKey;

		// Messages track when and who created the message
		public DateTime CreatedOn = DateTime.UtcNow.ToSpecificTimeZone(TZConvert.GetTimeZoneInfo("Central Standard Time"));
		public string CreatedBy;

		// Messages can contain exception and stack trace information
		public Exception Exception;

		// Messages contain caller information
		public string FilePath;
		public int LineNumber;

		public LogMessage()
		{
		}

		public bool IsValidLogLevel(LogLevel level)
		{
			return LogLevel <= level;
		}
	} 
	#endregion
	public class ConsoleLog : IDisposable
	{
		public List<string> Headers = new List<string> {"CreatedOn", "Message", "LogLevel"};
		public List<string> HeaderNames = new List<string> {"Time", "Message", "Level"};

		public LogLevel LogLevel = LogLevel.All;
		public Dictionary<string, int> Columns = new Dictionary<string, int>();
		public int TableWidth = 100;
		public int CellPadding = 1;
		public int LastRowSize;
		public int TotalLineCount;

		public string OutputFolder = @"C:\TEMP\Logs";
		public string OutputFileName = string.Empty;
		public string ProcessKey = Extensions.EncodedString.NewGuidString();
		public bool SaveOutputLogFile = true;
		public StringBuilder Content = new StringBuilder();
		public bool SaveOutput = true;
		public bool EchoConsole = true;
		public bool IndentOutput = true;
		public string IndentToken = "  ";
		public int IndentLevel;

		public bool HasOpened;
		public bool HasClosed;

		public int CurrentRow;

		private Encoding _encoding = new UTF8Encoding(false);

		#region Table Draw Styles

		protected char TopLeftCorner = '.';
		protected char TopMiddle = '-';
		protected char TopRightCorner = '.';
		protected char LeftEdge = '|';
		protected char CenterMiddle = '+';
		protected char RightEdge = '|';
		protected char BottomLeftCorner = '\'';
		protected char BottomMiddle = '-';
		protected char BottomRightCorner = '\'';
		protected char Middle = '-';
		protected char Center = '|';
		

		private void SetTableStyle(string style)
		{
			if (style.Length == 11)
			{
				TopLeftCorner = style[0];
				TopMiddle = style[1];
				TopRightCorner = style[2];
				LeftEdge = style[3];
				CenterMiddle = style[4];
				RightEdge = style[5];
				BottomLeftCorner = style[6];
				BottomMiddle = style[7];
				BottomRightCorner = style[8];
				Middle = style[9];
				Center = style[10];
			}
		}

		private string GetTableStyle()
		{
			var sb = new StringBuilder();
			sb.Append(TopLeftCorner);
			sb.Append(TopMiddle);
			sb.Append(TopRightCorner);
			sb.Append(LeftEdge);
			sb.Append(CenterMiddle);
			sb.Append(RightEdge);
			sb.Append(BottomLeftCorner);
			sb.Append(BottomMiddle);
			sb.Append(BottomRightCorner);
			sb.Append(Middle);
			sb.Append(Center);
			return sb.ToString();
		}

		public string TableStyle { get { return GetTableStyle(); } set {SetTableStyle(value);}}

		#endregion

		public ConsoleLog(string processName = null, string filePath = null)
		{
			try
			{
				Console.SetWindowSize(TableWidth, Console.WindowHeight);
			}
		    catch
		    {
		        // ignored
		    }

			OutputFileName = !string.IsNullOrEmpty(processName) ?  $"{processName}-{DateTime.Now.ToString("yyyyMMdd_hhmmss")}.log.txt" : $"{ProcessKey}-{DateTime.Now.ToString("yyyyMMdd_hhmmss")}.log.txt";
			if(!string.IsNullOrEmpty(filePath))
				OutputFolder = filePath;

			Columns = new Dictionary<string, int> {{"CreatedOn", 15}, {"LogLevel", 10}};			
			if (!Columns.ContainsKey("Message"))
				Columns["Message"] = TableWidth - Columns.Sum(x => Math.Abs(x.Value)) - Headers.Count - 1;
		}

		public void Open()
		{
			HasOpened = true;
			
			WriteRowBorder(true, false);
			WriteRowSpan();
			WriteLogHeader();
			WriteRowSpan();
			WriteRowContent('-', true);
			WriteRowContent(HeaderNames);
			WriteRowContent('=', true);
			TotalLineCount += 3;
		}

		public void Close()
		{
			WriteRowBorder(false, true);
			TotalLineCount ++;
		}

		public void WriteLogHeader()
		{
			WriteRowSpan();
		}

		public void WriteMessage(LogMessage message)
		{
			if (!HasOpened) Open();

			var columns = new List<string>();
			columns.Add(message.CreatedOn.ToString());
			columns.Add(message.Message.ToString());
			columns.Add(message.LogLevel.ToString());

			if (CurrentRow != 0)
			{
				if (LastRowSize > 1)
					WriteRowBorder(false, false);
				else 
				{
					WriteRowContent();
				}
			}

			WriteColumns(columns);
			CurrentRow ++;

		
		}	
		public string AlignColumnValue(string value, int width, bool alignLeft = true)
		{
			if (value.Length == width) return value;
			return value.Length > width
				? (alignLeft ? value.Substring(0, width) : value.Substring(width))
				: (alignLeft ? value.PadRight(width, ' ') : value.PadLeft(width, ' '));
		}

		public void WriteColumns(List<string> columns)
		{
			var stacks = new List<List<string>>();

			foreach (var item in columns.Select((v, i) => new {Index = i, Value = v}))
			{
				if (item.Index >= Headers.Count) continue;

				var colname = Headers[item.Index];
				var width = Math.Abs(Columns[colname]);
				stacks.Add(SplitColumnLines(width, item.Value).ToList());
			}

			var maxStack = stacks.Max(x => x.Count);
			LastRowSize = 0;
			for (var i = 0; i < maxStack; i++)
			{
				var row = stacks.Select(t => i >= t.Count ? "" : t[i]).ToList();
				WriteRowContent(row);
				LastRowSize ++;
				TotalLineCount ++;
			}
		}

		public void WriteRowBorder(bool top, bool bottom)
		{
			var sb = new StringBuilder();

			bool left = true;
			foreach (var colname in Headers)
			{
				var width = Math.Abs(Columns[colname]);

				if (left) sb.Append(top ? TopLeftCorner : bottom ? BottomLeftCorner : LeftEdge);
				else sb.Append(top ? TopMiddle : bottom ? BottomMiddle: CenterMiddle);
				
				left = false;
				sb.Append(Middle, width);
			}

			sb.Append(top ? TopRightCorner : bottom ? BottomRightCorner: RightEdge);
			
			Write(sb.ToString());
			TotalLineCount++;
		}

		public IEnumerable<string> SplitColumnLines(int width, string source)
		{
			width -= CellPadding * 2;

			var line = new StringBuilder();
			foreach (var c in source)
			{
				line.Append(c);
				if (line.Length == width)
				{
					var parts = SplitLastWordBreak(line.ToString());
					yield return parts[0];
					line.Clear();
					if (parts.Count > 1) line.Append(parts[1]);
				}
			}

			if (line.Length > 0)
				yield return line.ToString();
		}

		public List<string> SplitLastWordBreak(string source, double limit = .25, bool splitExtra = false)
		{
			StringBuilder extra = new StringBuilder();
			foreach(var c in source.Reverse())
			{
				if (char.IsWhiteSpace(c))
					break;

				if (splitExtra && char.IsSeparator(c))
					break;

				extra.Append(c);
			}

			var result = new List<string>();

			if (!splitExtra && extra.Length > (source.Length*limit))
				return SplitLastWordBreak(source, limit, true);

			if (extra.Length == source.Length || extra.Length > (source.Length * limit))
			{
				result.Add(source);
			}
			else
			{
				var pos = source.Length - extra.Length;
				result.Add(source.Substring(0, pos));
				result.Add(source.Substring(pos));
			}

			return result;
		}

		public void WriteRowSpan(string row="", bool center = true, int width=0, int minLines=1, int padding = 0, int indent = 0)
		{
			var maxWidth = TableWidth - 2 - CellPadding*2 - padding;
			if (width == 0 || width > maxWidth)
				width = maxWidth;

			var lines = SplitColumnLines(width, row).ToList();
			while (lines.Count < minLines) lines.Add("");

			foreach (var line in lines)
			{
				var sb = new StringBuilder();
				sb.Append(Center);

				var start = center ? (maxWidth - line.Length)/2 : CellPadding;
				start += padding;

				if (start > 0)
					sb.Append(' ', start);

				sb.Append(line);

				var end = TableWidth - 2 - line.Length - start;
				if (end > 0)
					sb.Append(' ', end);
				sb.Append(Center);
				Write(sb.ToString());

				TotalLineCount ++;
			}
			
			
		}

		public void WriteRowContent(char padding = ' ', bool spanColumns=false)
		{
			var sb = new StringBuilder();

			bool left = true;
			foreach (var colname in Headers)
			{
				var width = Math.Abs(Columns[colname]);
				sb.Append(left ? Center : spanColumns ? padding : Center);
				left = false;
				sb.Append(padding, width);
			}

			sb.Append(Center);
			Write(sb.ToString());
		}

		public void WriteRowContent(List<string> columns)
		{
			var sb = new StringBuilder();

			foreach (var item in columns.Select((v, i) => new { Index = i, Value = v }))
			{
				if (item.Index >= Headers.Count) continue;

				sb.Append(Center);
				
				sb.Append(' ', CellPadding);
				
				var colname = Headers[item.Index];
				var width = Columns[colname];
				bool alignLeft = true;
				if (width < 0)
				{
					width *= -1;
					alignLeft = false;
				}
				width -= CellPadding*2;

				sb.Append(AlignColumnValue(item.Value, width, alignLeft));
				sb.Append(' ', CellPadding);
			}
			sb.Append(Center);
			Write(sb.ToString());
		}
		public void Write(string content)
		{
			if (IndentOutput)
			{
				content = Indent(content);
				if (SaveOutput) Content.AppendLine(content);
				if (EchoConsole) Console.WriteLine(content);
			}
			else
			{
				if (SaveOutput) Content.Append(content);
				if (EchoConsole) Console.Write(content);
			}
		}

		public void WriteLine(string content)
		{
			if (IndentOutput)
			{
				content = Indent(content);
			}

			if (SaveOutput) Content.AppendLine(content);
			if (EchoConsole) Console.WriteLine(content);
		}

		public string Indent(string source)
		{
			return source;
		}

		public void Dispose()
		{
			Close();

			if (SaveOutputLogFile)
				TrySaveOutputFile(GetOutputFile(OutputFileName), Content.ToString());
		}

		public bool TrySaveOutputFile(string filename, string content)
		{
			try
			{
				File.WriteAllText(filename, content, _encoding);
				Console.Error.WriteLine(filename);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public string GetOutputFile(string filename)
		{
			if (!string.IsNullOrEmpty(OutputFolder))
				Extensions.IO.TryCreateDirectory(OutputFolder);			

			return Path.Combine(OutputFolder, filename);
		}


		#region Message Handling Methods

		public void Exception(string message, Exception exception, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Error)
			{
				var msg = CreateMessage(LogLevel.Error, message, exception, content, args);
				HandleMessage(msg);
			}
		}

		public void WarningException(string message, Exception exception, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Warning)
			{
				var msg = CreateMessage(LogLevel.Warning, message, exception, content, args);
				HandleMessage(msg);
			}
		}

		public void FatalException(string message, Exception exception, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Fatal)
			{
				var msg = CreateMessage(LogLevel.Fatal, message, exception, content, args);
				HandleMessage(msg);
			}
		}

		public void CriticalException(string message, Exception exception, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Critical)
			{
				var msg = CreateMessage(LogLevel.Critical, message, exception, content, args);
				HandleMessage(msg);
			}
		}

		public void Event(string message, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Event)
			{
				var msg = CreateMessage(LogLevel.Error, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void Debug(string message, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Debug)
			{
				var msg = CreateMessage(LogLevel.Debug, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void Message(string message, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Debug)
			{
				var msg = CreateMessage(LogLevel.Message, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void Info(string message, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Info)
			{
				var msg = CreateMessage(LogLevel.Info, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public bool Assert(bool condition, string message, LogLevel logLevel = LogLevel.Assert, object content = null, params object[] args)
		{
			if (logLevel > LogLevel) return condition;

			if (!condition)
			{
				var msg = CreateMessage(logLevel, message, null, content, args);
				HandleMessage(msg);
			}

			return condition;
		}

		public void Warning(string message, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Warning)
			{
				var msg = CreateMessage(LogLevel.Warning, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void Error(string message, object content = null, params object[] args)
		{
			if (LogLevel >= LogLevel.Error)
			{
				var msg = CreateMessage(LogLevel.Error, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void Fatal(string message, object content = null, params object[] args)
		{


			if (LogLevel >= LogLevel.Fatal)
			{
				var msg = CreateMessage(LogLevel.Fatal, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void Critical(string message, object content = null, params object[] args)
		{

			if (LogLevel >= LogLevel.Critical)
			{
				var msg = CreateMessage(LogLevel.Critical, message, null, content, args);
				HandleMessage(msg);
			}
		}

		public void HandleMessage(LogMessage message)
		{
			if (message.LogLevel > LogLevel)
				return;
			
			WriteMessage(message);
		}

		public virtual LogMessage CreateMessage(LogLevel logLevel, string message, Exception exception = null, object content = null, params object[] args)
		{
			if (exception == null && content is Exception)
			{
				exception = (Exception)content;
				content = null;
			}

			var msg = new LogMessage
			{
				LogLevel = logLevel,
				Exception = exception,
			};

			if (string.IsNullOrEmpty(message))
			{
				msg.Message = "";
				return msg;
			}

			msg.Message = message;
			return msg;
		}
		#endregion
	}
}