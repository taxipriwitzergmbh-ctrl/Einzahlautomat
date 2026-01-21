using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class ZonePoly
    {
        private struct PolyPoint
        {
            public double lat;
            public double lng;

            public PolyPoint(double lat, double lng)
            {
                this.lat = lat;
                this.lng = lng;
            }
        }

        int mId;
        PolyPoint[] mPoints;

        public ZonePoly(int PolyId, IEnumerable<(double lat, double lng)> points)
        {
            mId = PolyId;
            mPoints = points?.Select(p => new PolyPoint(p.lat, p.lng)).ToArray() ?? new PolyPoint[0];
        }
    }
}
