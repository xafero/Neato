using System;
using System.Runtime.InteropServices;

namespace Neato
{
	public static class WinInterop
	{
		
		[DllImport("user32.dll")]
		static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		const int BM_CLICK = 0x00F5;

		public static void SendClick(IntPtr handle)
		{
			SendMessage(handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
		}
		
	}
}