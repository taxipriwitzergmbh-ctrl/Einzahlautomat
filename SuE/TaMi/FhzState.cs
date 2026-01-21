using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class FhzState
    {
        private int id;
        private string name;
        private string kürzel;
        private System.Drawing.Color textColor;
        private System.Drawing.Brush textBrush;

        private System.Drawing.Color backColor;
        private System.Drawing.Pen backPen; // DefaultStroke = new Pen(Color.FromArgb(144, Color.MidnightBlue));
        private System.Drawing.Brush backBrush;

        private Pen framePen;

        bool visibleOnMap;


        public FhzState()
        {
            this.id = 0;
            this.name = "unknown";
            this.kürzel = "u";

            this.textColor = Color.Black;
            this.textBrush = new SolidBrush(this.textColor);

            this.backColor = Color.White;
            this.backPen = new Pen(Color.FromArgb(144, backColor), 6);
            this.backBrush = new SolidBrush(this.backColor);

            this.framePen = Pens.Black;

            this.visibleOnMap = true;
        }


        public FhzState(int id, string name, string kürzel, Color textColor, Color backColor)
        {
            this.id = id;
            this.name = name;
            this.kürzel = kürzel;
            
            this.textColor = textColor;
            this.textBrush = new SolidBrush(this.textColor);

            this.backColor = backColor;
            this.backPen = new Pen(Color.FromArgb(144, this.backColor), 6);
            this.backBrush = new SolidBrush(this.backColor);

            this.framePen = Pens.Black;

            this.visibleOnMap = true;
        }


        public int Id
        {
            get { return this.id; }
            set { this.id = value; }
        }

        public string Name
        {
            get { return this.name; }
            set { this.name = value; }
        }

        public string Kürzel
        {
            get { return this.kürzel; }
            set { this.kürzel = value; }
        }

        public System.Drawing.Color TextColor
        {
            get { return this.textColor; }
            set
            {
                this.textColor = value;
                this.textBrush = new SolidBrush(this.textColor);
            }
        }

        public System.Drawing.Brush TextBrush
        {
            get { return this.textBrush; }
        }

        public System.Drawing.Color BackColor
        {
            get { return this.backColor; }
            set {
                    this.backColor = value;
                    this.backPen = new Pen(Color.FromArgb(230, this.backColor), 6);
                    this.backBrush = new SolidBrush(this.backColor);
                }
        }

        public System.Drawing.Pen BackPen
        {
            get { return this.backPen; }
        }

        public System.Drawing.Brush BackBrush
        {
            get { return this.backBrush; }
        }

        public System.Drawing.Pen FramePen
        {
            get { return this.framePen; }
        }

        

        public bool VisibleOnMap
        {
            get { return this.visibleOnMap; }
            set { this.visibleOnMap = value; }
        }



    }
}
