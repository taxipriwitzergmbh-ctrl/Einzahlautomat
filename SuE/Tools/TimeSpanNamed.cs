using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.Tools
{
    public class TimeSpanNamed
    {
        public TimeSpanNamed()
        {
        }

        public TimeSpanNamed(string name, TimeSpan timespan)
        {
            this.name = name;
            this.timespan = timespan;
        }

        private TimeSpan timespan;
        public TimeSpan TimeSpan
        {
            get { return timespan; }
            set { timespan = value; }
        }

        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public override string ToString()
        {
            return string.Format("name: '{0}', timespan: {1}", name, timespan);
        }
    }
}
