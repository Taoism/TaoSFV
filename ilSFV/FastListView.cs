﻿using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ilSFV
{
	internal class FastListView : ListView
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct NMHDR
		{
			public IntPtr hwndFrom;
			public UIntPtr idFrom;
			public uint code;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int left, top, right, bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NMLVCUSTOMDRAW
		{
			public NMHDR hdr;
			public uint dwDrawStage;
			public IntPtr hdc;
			public RECT rc;
			public UIntPtr dwItemSpec;
			public uint uItemState;
			public IntPtr lItemlParam;
			public int clrText;
			public int clrTextBk;
			public int iSubItem;
			public uint dwItemType;
			public int clrFace;
			public int iIconEffect;
			public int iIconPhase;
			public int iPartId;
			public int iStateId;
			public RECT rcText;
			public uint uAlign;
		}

		private const int OCM_NOTIFY = 0x204E;
		private const uint NM_CUSTOMDRAW = unchecked((uint)-12);
		private const uint CDDS_PREPAINT = 0x1;
		private const uint LVCDI_GROUP = 0x1;
		private const int CDRF_DODEFAULT = 0x0;
		private const int CDRF_SKIPDEFAULT = 0x4;

		private static readonly PropertyInfo _groupIdProperty = typeof(ListViewGroup).GetProperty("ID", BindingFlags.Instance | BindingFlags.NonPublic);

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == OCM_NOTIFY)
			{
				NMHDR hdr = Marshal.PtrToStructure<NMHDR>(m.LParam);
				if (hdr.code == NM_CUSTOMDRAW)
				{
					if (!Application.IsDarkModeEnabled)
					{
						// skip WinForms' per-item custom draw for speed
						m.Result = (IntPtr)CDRF_DODEFAULT;
						return;
					}

					// dark mode item colors come from WinForms' custom draw, but group headers stay dark blue; draw those here
					NMLVCUSTOMDRAW cd = Marshal.PtrToStructure<NMLVCUSTOMDRAW>(m.LParam);
					if (cd.dwDrawStage == CDDS_PREPAINT && cd.dwItemType == LVCDI_GROUP && DrawGroupHeader(cd))
					{
						m.Result = (IntPtr)CDRF_SKIPDEFAULT;
						return;
					}
				}
			}

			base.WndProc(ref m);
		}

		private bool DrawGroupHeader(NMLVCUSTOMDRAW cd)
		{
			ListViewGroup group = FindGroup((int)cd.dwItemSpec.ToUInt32());
			if (group == null)
				return false;

			Rectangle rect = Rectangle.FromLTRB(cd.rcText.left, cd.rcText.top, cd.rcText.right, cd.rcText.bottom);
			using (Graphics g = Graphics.FromHdc(cd.hdc))
			{
				using (SolidBrush back = new SolidBrush(BackColor))
					g.FillRectangle(back, rect);

				Rectangle textRect = new Rectangle(rect.X + 10, rect.Y, rect.Width - 10, rect.Height);
				Color textColor = Color.FromArgb(0x99, 0xC8, 0xFF);
				TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
				TextRenderer.DrawText(g, group.Header, Font, textRect, textColor, flags);

				int textWidth = TextRenderer.MeasureText(g, group.Header, Font, textRect.Size, flags).Width;
				int lineX = textRect.X + textWidth + 6;
				int lineY = rect.Y + rect.Height / 2;
				if (lineX < rect.Right - 4)
				{
					using (Pen line = new Pen(SystemColors.ControlDark))
						g.DrawLine(line, lineX, lineY, rect.Right - 4, lineY);
				}
			}

			return true;
		}

		private ListViewGroup FindGroup(int id)
		{
			if (_groupIdProperty == null)
				return null;

			foreach (ListViewGroup group in Groups)
			{
				if ((int)_groupIdProperty.GetValue(group) == id)
					return group;
			}

			return null;
		}
	}
}
