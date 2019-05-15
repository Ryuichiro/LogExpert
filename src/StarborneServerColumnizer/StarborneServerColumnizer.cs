using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LogExpert;

namespace Starborne
{
    /// <summary>
    ///     This Columnizer can parse Starborne Server log files.
    /// </summary>
    public class ServerColumnizer : ILogLineColumnizer
    {
        #region Fields

        protected const string DATETIME_FORMAT = "yyyy-MM-dd HH:mm:ss,fff";
        protected CultureInfo cultureInfo = new CultureInfo("en-US");
        protected int timeOffset = 0;

        #endregion

        #region cTor

        public ServerColumnizer()
        {
        }

        #endregion

        #region Public methods

        public bool IsTimeshiftImplemented()
        {
            return true;
        }

        public void SetTimeOffset(int msecOffset)
        {
            this.timeOffset = msecOffset;
        }

        public int GetTimeOffset()
        {
            return this.timeOffset;
        }


        public DateTime GetTimestamp(ILogLineColumnizerCallback callback, ILogLine line)
        {
            IColumnizedLogLine cols = SplitLine(callback, line);
            if (cols == null || cols.ColumnValues == null || cols.ColumnValues.Length < 2)
            {
                return DateTime.MinValue;
            }
            if (cols.ColumnValues[0].FullValue.Length == 0 || cols.ColumnValues[1].FullValue.Length == 0)
            {
                return DateTime.MinValue;
            }

            try
            {
                DateTime dateTime = DateTime.ParseExact( cols.ColumnValues[0].FullValue + " " + cols.ColumnValues[1].FullValue, DATETIME_FORMAT, this.cultureInfo);
                return dateTime;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }


        public void PushValue(ILogLineColumnizerCallback callback, int column, string value, string oldValue)
        {
            if (column == 1)
            {
                try
                {
                    DateTime newDateTime = DateTime.ParseExact(value, DATETIME_FORMAT, this.cultureInfo);
                    DateTime oldDateTime = DateTime.ParseExact(oldValue, DATETIME_FORMAT, this.cultureInfo);
                    long mSecsOld = oldDateTime.Ticks / TimeSpan.TicksPerMillisecond;
                    long mSecsNew = newDateTime.Ticks / TimeSpan.TicksPerMillisecond;
                    this.timeOffset = (int)(mSecsNew - mSecsOld);
                }
                catch (FormatException)
                {
                }
            }
        }


        public string GetName()
        {
            return "Starborne Server Columnizer";
        }

        public string GetDescription()
        {
            return "Splits every line into: Date, Time, Host, App, Thread, Context, Severity, Category, Activity, PlayerGlobalId, PlayerId, EmpireId, Task and the rest of the log message";
        }

        public int GetColumnCount()
        {
            return 14;
        }

        public string[] GetColumnNames()
        {
            return new string[] { "Date", "Time", "Host", "App", "Thread", "Context", "Severity", "Category", "Activity", "PlayerGlobalId", "PlayerId", "EmpireId", "Class", "Message" };
        }

        public IColumnizedLogLine SplitLine(ILogLineColumnizerCallback callback, ILogLine line)
        {
            ColumnizedLogLine columnizedLogLine = new ColumnizedLogLine();
            columnizedLogLine.LogLine = line;
            columnizedLogLine.ColumnValues = new IColumn[14];

            Column[] columns;
            if (line.FullLine.StartsWith("20"))
            {
                string[] dateTime = line.FullLine.Split(new char[] { ' ' }, 3);
                string[] splitString = line.FullLine.Split(']');
                string[] values = splitString.Where(x => x.Contains('[')).Select(x => x.Substring(x.IndexOf('[')+1)).ToArray();
                string message = splitString.Last().Trim();

                columns = new Column[14]
                {
                    new Column // Date
                    {
                        FullValue = dateTime[0] ,
                        Parent = columnizedLogLine
                    },
                    new Column // Time
                    {
                        FullValue = dateTime[1] ,
                        Parent = columnizedLogLine
                    },
                    new Column // Host
                    {
                        FullValue =  GetValue(values, "Host", 1),
                        Parent = columnizedLogLine
                    },
                    new Column // Application
                    {
                        FullValue =  GetValue(values, "App", 2),
                        Parent = columnizedLogLine
                    },
                    new Column // Thread
                    {
                        FullValue =  GetValue(values, "TID", 3),
                        Parent = columnizedLogLine
                    },
                    new Column // Context
                    {
                        FullValue = GetValue(values, "Ctx", 4) ,
                        Parent = columnizedLogLine
                    },
                    new Column // Severity
                    {
                        FullValue = GetValue(values, "lvl", 5) ,
                        Parent = columnizedLogLine
                    },
                    new Column // Category
                    {
                        FullValue = GetValue(values, "Cat", 6) ,
                        Parent = columnizedLogLine
                    },
                    new Column // Activity
                    {
                        FullValue = GetValue(values, "Act", 7) ,
                        Parent = columnizedLogLine
                    },
                    new Column // PlayerGolbalId
                    {
                        FullValue = GetValue(values, "PlayerGlobalId", 8) ,
                        Parent = columnizedLogLine
                    },
                    new Column // PlayerId
                    {
                        FullValue = GetValue(values, "PlayerId", 9) ,
                        Parent = columnizedLogLine
                    },
                    new Column // EmpireId
                    {
                        FullValue = GetValue(values, "EmpireId", 10) ,
                        Parent = columnizedLogLine
                    },
                    new Column // Class
                    {
                        FullValue = GetValue(values, "class", 11) ,
                        Parent = columnizedLogLine
                    },
                    new Column // Message
                    {
                        FullValue = message ,
                        Parent = columnizedLogLine
                    },
                };
            } else
            {
                columns = new Column[14]
                {
                    new Column // Date
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Time
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Host
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Application
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Thread
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Context
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Severity
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Category
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Activity
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // PlayerGolbalId
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // PlayerId
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // EmpireId
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Taks
                    {
                        FullValue = "" ,
                        Parent = columnizedLogLine
                    },
                    new Column // Message
                    {
                        FullValue = line.FullLine ,
                        Parent = columnizedLogLine
                    },
                };
            }

            columnizedLogLine.ColumnValues = columns.Select(a => a as IColumn).ToArray(); // Is this select necessary ?

            return columnizedLogLine;
        }

        public Priority GetPriority(string fileName, IEnumerable<ILogLine> samples)
        {
            Priority result = Priority.NotSupport;

            if (fileName == "Prosper.log")
            {
                result = Priority.PerfectlySupport;
            }

            return result;
        }

        public string GetValue(string[] ar, int id)
        {
            if (ar.Length <= id || id < 1)
                return "";

            var val = ar[id - 1];
            var pos = val.IndexOf('=');

            if (pos >= 0)
                val = val.Substring(pos + 1).Trim();

            return val;
        }

        public string GetValue(string[] ar, string str)
        {
            var val = ar.Where(x => x.StartsWith(str)).ToArray();

            if (val.Length < 1)
                return "";

            var pos = val[0].IndexOf('=');

            if (pos >= 0)
                val[0] = val[0].Substring(pos + 1).Trim();

            return val[0];
        }

        public string GetValue(string[] ar, string str, int id)
        {
            var val = GetValue(ar, str);

            if (val == "")
                val = GetValue(ar, id);

            return val;
        }

        #endregion
    }
}