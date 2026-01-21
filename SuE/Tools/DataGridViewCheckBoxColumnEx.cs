using System;
using System.Windows.Forms;
using System.Drawing;

namespace SuE.Tools
{
    public class DataGridViewCheckBoxColumnEx : DataGridViewCheckBoxColumn
    {
        // Zusammenfassung: Initialisiert eine neue Instanz der System.Windows.Forms.DataGridViewTextBoxColumn-Klasse im Standardzustand.
        public DataGridViewCheckBoxColumnEx() : base()
        {
            HeaderCell.Style.WrapMode = DataGridViewTriState.False;
        }

        public override DataGridViewCell CellTemplate
        {
            get
            {
                return new DataGridViewCheckBoxCellEx();
            }
        }
    }

    public class DataGridViewCheckBoxCellEx : DataGridViewCheckBoxCell
    {
        //private static  Rectangle tempLabelRect;
        private const   int PAD_RIGHT_FROM_CHECKBOX = 0;

        public string   Label;
        public string   SubText;
        public Font     SubTextFont = null;
        public Color    SubtextColor = Color.Transparent;


        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
        {
            //Hide the selection
            elementState = elementState & ~DataGridViewElementStates.Selected;

            // the base Paint implementation paints the check box
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);

            // Get the check box bounds: they are the content bounds
            Rectangle contentBounds = this.GetContentBounds(rowIndex);


            StringFormat format = new StringFormat(StringFormat.GenericDefault);
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.Hide;
            format.FormatFlags |= StringFormatFlags.NoWrap;

            Rectangle textRect = new Rectangle();
            Font textFont;
            Brush textBrush;

            //CellStyle
            if (this.HasStyle)
            {
                //Font and Color
                textFont = cellStyle.Font;
                textBrush = new SolidBrush(elementState.HasFlag(DataGridViewElementStates.Selected) ? cellStyle.SelectionForeColor : cellStyle.ForeColor);

                //Aligment
                //format.Alignment = (cellStyle.Alignment == DataGridViewContentAlignment.TopLeft) ? StringAlignment.Near :  //MiddleLeft
                //                   (cellStyle.Alignment == DataGridViewContentAlignment.TopRight) ? StringAlignment.Far : // MiddleRight
                //                   StringAlignment.Center;

        /*
        // Zusammenfassung:
        NotSet = 0,
        TopLeft = 1,
        TopCenter = 2,
        TopRight = 4,
        MiddleLeft = 16,
        MiddleCenter = 32,
        MiddleRight = 64,
        BottomLeft = 256,
        BottomCenter = 512,
        BottomRight = 1024,
        */


                //Horizontal Align
                /*
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
                }*/

                //Text soll immer rechts sein
                format.Alignment = StringAlignment.Far;

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

                //Rect
                textRect.X = cellBounds.X + contentBounds.Right + PAD_RIGHT_FROM_CHECKBOX;
                textRect.Y = cellBounds.Y + 2; //cellStyle.Padding.Top;

                textRect.Height = cellBounds.Height - 2 - cellStyle.Padding.Bottom;
                textRect.Width = cellBounds.Width - 2 - contentBounds.Right - PAD_RIGHT_FROM_CHECKBOX - 2; //- cellStyle.Padding.Right;
            }

            //No CellStyle use Control Defaults
            else
            {
                //Font and Color
                textFont = this.DataGridView.Font; // Control.DefaultFont;
                textBrush = new SolidBrush(this.Selected ? Control.DefaultForeColor : Control.DefaultForeColor);

                //Aligment
                format.Alignment = StringAlignment.Near;

                //Rect
                textRect.X = cellBounds.X + contentBounds.Right + PAD_RIGHT_FROM_CHECKBOX;
                textRect.Y = cellBounds.Y + 2;

                textRect.Height = cellBounds.Height - 2;
                textRect.Width = cellBounds.Width - 2 - contentBounds.Right - PAD_RIGHT_FROM_CHECKBOX - 2;
            }

            //INFO: Diese CPU Zeit können wir uns sparen glaube ich
            //if (graphics.MeasureString((String)e.Value, e.CellStyle.Font, e.CellBounds.Location, format).Width >= e.CellBounds.Width)
            //            if (format.Alignment == StringAlignment.Center) format.Alignment = StringAlignment.Near;

            int fontLineHeight = textFont.Height;

            graphics.DrawString(this.Label, textFont, textBrush, textRect, format);


            //DEBUG RECT's
            /*
                        graphics.DrawRectangle(new Pen(Brushes.Red), cellBounds);
                        graphics.DrawRectangle(new Pen(Brushes.Blue), tempLabelRect );
            */


            if (SubText != null && SubText.Length > 0)
            {
                if (SubtextColor != Color.Transparent)
                    ((SolidBrush)textBrush).Color = SubtextColor;
                //textBrush = new SolidBrush(this.Selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor);

                //textBrush = new SolidBrush(this.Selected ? cellStyle.SelectionForeColor : Color.DarkGray);

                textRect.Y += fontLineHeight;
                textRect.Height -= fontLineHeight - 1;

                graphics.DrawString(SubText, (SubTextFont != null ? SubTextFont : textFont), textBrush, textRect, format);

                //Debug Rect
                //graphics.DrawRectangle(new Pen(Brushes.Blue), textRect);
            }

            
        }

    }

}


