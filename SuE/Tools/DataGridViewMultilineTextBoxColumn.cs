using System;
using System.Windows.Forms;
using System.Drawing;

namespace SuE.Tools
{
    public class DataGridViewMultilineTextBoxColumn : DataGridViewTextBoxColumn
    {
        // Zusammenfassung: Initialisiert eine neue Instanz der System.Windows.Forms.DataGridViewTextBoxColumn-Klasse im Standardzustand.
        public DataGridViewMultilineTextBoxColumn() : base ()
        {
            HeaderCell.Style.WrapMode = DataGridViewTriState.False;
        }

        public override DataGridViewCell CellTemplate
        {
            get
            {
                return new DataGridViewMultilineTextBoxCell();
            }
        }
    }


    //Die Zelle wird so gezeichnet das kein Wortumbruch außer durch CRLF gemacht wird. Das klappt beim normalen DataGridViewTextBoxColumn nicht
    public class DataGridViewMultilineTextBoxCell : DataGridViewTextBoxCell
    {
        public string   SubText;
        public Font     SubTextFont = null;
        public Color    SubtextColor = Color.Transparent;



        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
        {
            // the base Paint implementation paints the check box
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, null, null, errorText, cellStyle, advancedBorderStyle, paintParts);

            // Get the check box bounds: they are the content bounds
            //Rectangle contentBounds = this.GetContentBounds(rowIndex);


            //INFO: Diese CPU Zeit können wir uns sparen glaube ich
            //if (graphics.MeasureString((String)e.Value, e.CellStyle.Font, e.CellBounds.Location, format).Width >= e.CellBounds.Width)
            //            if (format.Alignment == StringAlignment.Center) format.Alignment = StringAlignment.Near;

            StringFormat format = new StringFormat(StringFormat.GenericDefault);
            format.Trimming = StringTrimming.None;
            format.HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.Hide;
            format.FormatFlags |= StringFormatFlags.NoWrap;
            
            
            Font textFont;
            Brush textBrush;
            Rectangle textRect = new Rectangle(cellBounds.Location, cellBounds.Size);
            textRect.Width -= 2;
            textRect.Height -= 2;
            textRect.Inflate(-2, -2);

            /*
            Padding padding = new Padding(2);

            textRect.Y += padding.Top;
            textRect.X += padding.Left;
            textRect.Height -= padding.Bottom * 2;
            textRect.Width -= padding.Right * 2;
            */ 
             
            //textRect.Offset(padding.Left, padding.Top);
            //textRect.Inflate(padding.Right, padding.Bottom);

            //CellStyle
            if (this.HasStyle)
            {
                //Font and Color
                textFont = cellStyle.Font;
                textBrush = new SolidBrush(this.Selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor);

                //Horizontal Align
                switch (cellStyle.Alignment)
                {
                    case DataGridViewContentAlignment.TopCenter:
                    case DataGridViewContentAlignment.MiddleCenter:
                    case DataGridViewContentAlignment.BottomCenter:
                        format.Alignment = StringAlignment.Center;
                        break;

                    case DataGridViewContentAlignment.TopRight:
                    case DataGridViewContentAlignment.MiddleRight:
                    case DataGridViewContentAlignment.BottomRight:
                        format.Alignment = StringAlignment.Far;
                        break;

                    default:
                        format.Alignment = StringAlignment.Near;
                        break;
                }

                //Vertical Align
                switch (cellStyle.Alignment)
                {
                    case DataGridViewContentAlignment.MiddleLeft:
                    case DataGridViewContentAlignment.MiddleCenter:
                    case DataGridViewContentAlignment.MiddleRight:
                        format.LineAlignment = StringAlignment.Near;
                        break;

                    case DataGridViewContentAlignment.BottomLeft:
                    case DataGridViewContentAlignment.BottomCenter:
                    case DataGridViewContentAlignment.BottomRight:
                        format.LineAlignment = StringAlignment.Far;
                        break;

                    default:
                        format.LineAlignment = StringAlignment.Near;
                        break;
                }

            }

            //No CellStyle use Control Defaults
            else
            {
                //Font and Color
                textFont = this.DataGridView.Font; // Control.DefaultFont;
                textBrush = new SolidBrush(this.Selected ? Control.DefaultForeColor : Control.DefaultForeColor);

                //Aligment
                format.Alignment = StringAlignment.Near;
            }


            int fontLineHeight = textFont.Height;

            //this.DataGridView.DefaultCellStyle.Padding

            //Debug.WriteLine("Drawing Text: '" + formattedValue.ToString() + "' Font: " + textFont);

            //Text zeichnen
            graphics.DrawString(formattedValue.ToString(), textFont, textBrush, textRect, format);

            //DEBUG RECT's
            //graphics.DrawRectangle(new Pen(Brushes.Red), textRect);


            if (!string.IsNullOrEmpty(SubText))
            {
                if (SubtextColor != Color.Transparent)
                    ((SolidBrush)textBrush).Color = SubtextColor;
                    //textBrush = new SolidBrush(this.Selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor);

                //textBrush = new SolidBrush(this.Selected ? cellStyle.SelectionForeColor : Color.DarkGray);

                textRect.Y += fontLineHeight;
                textRect.Height -= fontLineHeight - 1;

                format.LineAlignment = StringAlignment.Far;
                graphics.DrawString(SubText, (SubTextFont != null ? SubTextFont : textFont), textBrush, textRect, format);

                //Debug Rect
                //graphics.DrawRectangle(new Pen(Brushes.Blue), textRect);
            }

        }

    }

}


