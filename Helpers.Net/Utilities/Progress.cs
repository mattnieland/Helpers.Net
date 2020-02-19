using Helpers.Net.Loggers;
using System;
using System.Text;
using System.Threading;

namespace Helpers.Net.Utilities
{
	public class Progress : IDisposable
	{
		public ConsoleLog Log;
		public LogLevel StartLogLevel = LogLevel.Info;
		public LogLevel UpdateLogLevel = LogLevel.Monitor;
		public LogLevel StopLogLevel = LogLevel.Info;

		public string ProcessSourcePath;
		public string ProcessSourceKey;
		public string ProcessTaskId;

		public string ProcessName;
		public int TotalEvents;
		public DateTime ProcessStartTime;
		public DateTime LastEventTime;
		public TimeSpan LastElapsedTime;
		public TimeSpan TotalElapsedTime;

		public double CompletePercent = 0.0;
		public double LastEventRate;
		public double AverageEventRate;

		public bool ReportAverageRate = true;
		public bool ReportLastRate = false;
		public bool ReportElapsedTime = true;

		public TimeSpan AverageEstimateRemaining;
		public TimeSpan LastEstimateRemaining;

		public double StartReportInterval = .2;
		public TimeSpan TargetReportSpan = TimeSpan.Zero;
		public bool UseLastRateUpdateInterval = true;
		public bool UseManualReportInterval = false;
		public int ReportInterval = 1000;
		public int NextReportCount = 0;
		public int EventCount = 0;
		public string ProcessEventId = "";

		public Action<Progress> OnStart;
		public Action<Progress> OnUpdate;
		public Action<Progress> OnInterval;
		public Action<Progress> OnStop;

		public Func<Progress, string> StartReport;
		public Func<Progress, string> UpdateReport;
		public Func<Progress, string> StopReport;

		public Func<int, string> EventName;
		public Func<int, TimeSpan, double> RateReport;

		protected bool IsStarted;
		protected bool AutoSetReportInterval;

		public Progress(ConsoleLog log)
		{
			Log = log;
			IsStarted = false;
		}

		public Progress(string processName, int totalEvents = 0, ConsoleLog log = null)
		{
			if(log != null)
				Log = log;
			IsStarted = false;
			Start(processName, totalEvents);
		}

		public void Dispose()
		{
			Stop(cancel:true);
		}

		public void Start(string processName, int totalEvents=0)
		{
			if (IsStarted && EventCount > 0)
				Stop();

			ProcessName = processName;
			EventCount = 0;
			ProcessStartTime = DateTime.Now;
			LastEventTime = ProcessStartTime;
			CompletePercent = 0.0;
			AverageEstimateRemaining = new TimeSpan(0);
			AverageEventRate = 0.0;
			LastEstimateRemaining = new TimeSpan(0);
			LastEventRate = 0.0;

			AutoSetReportInterval = TargetReportSpan != TimeSpan.Zero;
			
			TotalEvents = totalEvents;
			if (!UseManualReportInterval)
			{
				if (totalEvents != 0)
				{
					if (StartReportInterval < 1.0)
						ReportInterval = (int) Math.Floor(totalEvents*StartReportInterval);
					else
						ReportInterval = (int) StartReportInterval;
				}
				else if (StartReportInterval > 1.0)
					ReportInterval = (int) StartReportInterval;
				else
					ReportInterval = 1000;
			}

			NextReportCount = ReportInterval;
			ProcessSourceKey = Extensions.EncodedString.NewGuidString();

			if (StartLogLevel != LogLevel.None)
			{
				string message = GetStartReport();
				if (StartLogLevel != LogLevel.Monitor)
				{
					var msg = new LogMessage
					{
						Message = message,
						LogLevel = StartLogLevel,
					};

					Log.HandleMessage(msg);
				}
			}
			IsStarted = true;

			if (OnStart != null)
				OnStart(this);
		}

		public void Update(int count = 1, bool report=true)
		{
			if (!IsStarted)
				return;

			Interlocked.Add(ref EventCount, count);

			if (OnUpdate != null)
				OnUpdate(this);

			if (!report) return;
			report = EventCount >= NextReportCount;

			if (AutoSetReportInterval && EventCount > 0)
			{
				var elapsed = DateTime.Now - ProcessStartTime;
				if (elapsed.TotalMinutes > TargetReportSpan.TotalMinutes / 2.0)
				{
					UpdateRate();
					NextReportCount = ReportInterval;
					report = EventCount >= NextReportCount;
				}
			}

			if (!report) return;
			
			UpdateRate();	
			NextReportCount =  EventCount + ReportInterval;
			
			if (UpdateLogLevel != LogLevel.None)
			{
				string msg = GetUpdateReport();
				Log.HandleMessage(new LogMessage {Message = msg, LogLevel = UpdateLogLevel});				
			}
			
			if (OnInterval != null)
				OnInterval(this);
		}

		public void Stop(bool cancel=false)
		{
			if (!IsStarted)
				return;

			IsStarted = false;

			if (cancel || EventCount == 0)
				return;

			UpdateRate();
			if (StopLogLevel != LogLevel.None)
			{
				var message = GetStopReport();

				if (StopLogLevel != LogLevel.Monitor)
				{
					var msg = new LogMessage
					{
						Message = message,
						LogLevel = StopLogLevel,
					};

					Log.HandleMessage(msg);
				}
			}

			if (OnStop != null)
				OnStop(this);
		}

		protected virtual string GetStartReport()
		{
			if (StartReport != null)
				return StartReport(this);
			
			var sb = new StringBuilder();

			sb.AppendFormat("{0}", ProcessName);
			if (TotalEvents > 0)
			{
				sb.AppendFormat(" (Total: {0}{1})", 
					TotalEvents,
					EventName != null ? string.Format(" {0}", EventName(TotalEvents)) : "" );
			}

			return sb.ToString();
		}

		protected virtual string GetUpdateReport()
		{
			if (UpdateReport != null)
				return UpdateReport(this);

			var sb = new StringBuilder();
			sb.AppendFormat("{0}: Processed {1}", ProcessName, EventCount);

			if (EventName != null)
				sb.AppendFormat(" {0}", EventName(EventCount));

			if (TotalEvents > 0 && EventCount > 0)
			{
				sb.AppendFormat(" of {0} ({1:F2}% Complete)", TotalEvents, CompletePercent);

				if (RateReport != null)
				{
					var elapsed = DateTime.Now - (ReportLastRate ? LastEventTime : ProcessStartTime);
					sb.AppendFormat(" {0}", RateReport(EventCount, elapsed));
				}
				else
				{
					if (ReportAverageRate)
					{
						sb.AppendFormat(" at {0:F2} / min", AverageEventRate);
					}
					else if (ReportLastRate)
					{
						sb.AppendFormat(" at {0:F2} / min", LastEventRate);
					}
				}

				if (ReportElapsedTime)
				{
					var remaining = ReportLastRate ? LastEstimateRemaining : AverageEstimateRemaining;
					if (remaining.TotalMinutes >= 60)
						sb.AppendFormat(" with {0:F2} hours remaining", remaining.TotalHours);
					else if (remaining.TotalSeconds >= 60)
						sb.AppendFormat(" with {0:F2} minutes remaining", remaining.TotalMinutes);
					else
						sb.AppendFormat(" with {0:F2} seconds remaining", remaining.TotalSeconds);
				}
			}
			else if (ReportElapsedTime)
			{
				var elapsed = DateTime.Now - (ReportLastRate ? LastEventTime : ProcessStartTime);
				LastElapsedTime = elapsed;

				if (elapsed.TotalMinutes >= 60)
					sb.AppendFormat(" in {0:F2} hours ", elapsed.TotalHours);
				else if (elapsed.TotalSeconds >= 60)
					sb.AppendFormat(" in {0:F2} minutes ", elapsed.TotalMinutes);
				else
					sb.AppendFormat(" in {0:F2} seconds ", elapsed.TotalSeconds);

				sb.Append(ReportLastRate ? " (interval)" : " total");
			}

			return sb.ToString();
		}

		protected virtual string GetStopReport()
		{
			if (StopReport != null)
				return StopReport(this);

			var sb = new StringBuilder();

			sb.AppendFormat("{0}: Completed {1}", ProcessName, EventCount);
			if (EventName != null)
				sb.AppendFormat(" {0}", EventName(EventCount));

			if (ReportElapsedTime && EventCount > 0)
			{
				var elapsed = DateTime.Now - ProcessStartTime;

				if (ReportAverageRate || ReportLastRate)
				{
					if (RateReport != null)
						sb.AppendFormat(" {0}", RateReport(EventCount, elapsed));
					else
						sb.AppendFormat(" at {0:F2} / min", EventCount / elapsed.TotalMinutes);
				}


				if (elapsed.TotalMinutes >= 60)
					sb.AppendFormat(" in {0:F2} hours total ", elapsed.TotalHours);
				else if (elapsed.TotalSeconds >= 60)
					sb.AppendFormat(" in {0:F2} minutes total", elapsed.TotalMinutes);
				else
					sb.AppendFormat(" in {0:F2} seconds total", elapsed.TotalSeconds);
			}
			
			return sb.ToString();
		}

		protected virtual void UpdateRate()
		{
			AutoSetReportInterval = false;

			var now = DateTime.Now;

			TotalElapsedTime = now - ProcessStartTime;
			LastElapsedTime = now - LastEventTime;


			LastEventTime = now;
			AverageEventRate = EventCount / TotalElapsedTime.TotalMinutes;
			LastEventRate = EventCount / LastElapsedTime.TotalMinutes;
		

			if (TargetReportSpan != TimeSpan.Zero)
			{
				var rate = UseLastRateUpdateInterval ? LastEventRate : AverageEventRate;
				ReportInterval = (int) Math.Floor(rate * TargetReportSpan.TotalMinutes);
			}

			if (TotalEvents > 0)
			{
				CompletePercent = 100.0 * (EventCount / (double)TotalEvents);

				var remaining = TotalEvents - EventCount;
				if (remaining >= 0)
				{
					AverageEstimateRemaining = AverageEventRate > 0 ? TimeSpan.FromMinutes(remaining/AverageEventRate) : TimeSpan.Zero;
					LastEstimateRemaining = LastEventRate > 0 ? TimeSpan.FromMinutes(remaining / LastEventRate) : TimeSpan.Zero;
				}
				else
				{
					AverageEstimateRemaining = TimeSpan.Zero;
					LastEstimateRemaining = TimeSpan.Zero;
				}
			}
			else
			{
				CompletePercent = 0;
				AverageEstimateRemaining = TimeSpan.Zero; 
				LastEstimateRemaining = TimeSpan.Zero;
			}

		}

		public static Progress operator ++(Progress a)
		{
			a.Update();
			return a;
		}

	}
}
