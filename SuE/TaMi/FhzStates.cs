using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class FhzStates
    {
        private readonly SortedList<int, FhzState> states;

        public static readonly FhzState DEFAULT_STATE = new FhzState(0, "UNBEKANNT", "UB", System.Drawing.Color.Black, System.Drawing.Color.White);

        public FhzStates()
        {
            this.states = new SortedList<int, FhzState>(20);
        }

        public void Add(FhzState fhzstate)
        {
            this.states.Add(fhzstate.Id, fhzstate);
        }

        public FhzState Get(int Id)
        {
            FhzState fhzstate = null;

            this.states.TryGetValue(Id, out fhzstate);

            if (fhzstate == null)
                return FhzStates.DEFAULT_STATE;

            return fhzstate;
        }

        public FhzState Remove(int Id)
        {
            FhzState fhzstate = null;

            this.states.TryGetValue(Id, out fhzstate);
            this.states.Remove(Id);

            return fhzstate;
        }

        public int Count
        {
            get { return this.states.Count; }
        }


    }
}
