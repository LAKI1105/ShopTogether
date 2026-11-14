// Test launcher only: production game code does not reference this file.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public sealed class ShopTogetherTestDesktop : IDisposable {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct StartupInfo {
        public int cb; public string reserved, desktop, title;
        public int x, y, xSize, ySize, xChars, yChars, fill, flags;
        public short show, reserved2Count; public IntPtr reserved2, stdIn, stdOut, stdErr;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr process, thread; public int processId, threadId; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError=true)] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr GetThreadDesktop(int threadId);
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    delegate bool EnumWindowProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll", SetLastError=true)] static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindowProc callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", SetLastError=true)] static extern bool PostMessage(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out int needed);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string workingDirectory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    readonly string name="SMC_Test_"+Guid.NewGuid().ToString("N");
    IntPtr desktop;
    static string DesktopName(IntPtr handle) {
        var text=new StringBuilder(512);int needed;
        if(handle==IntPtr.Zero || !GetUserObjectInformation(handle,2,text,text.Capacity*2,out needed)){int error=Marshal.GetLastWin32Error();throw new Win32Exception(error,"Could not inspect desktop (Win32 "+error+")");}
        return text.ToString();
    }
    static bool HasWindow(IntPtr desktop,int processId) {
        bool found=false;
        EnumWindowProc callback=(window,state)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner==(uint)processId){found=true;return false;}return true;};
        SetLastError(0);bool enumerated=EnumDesktopWindows(desktop,callback,IntPtr.Zero);int error=Marshal.GetLastWin32Error();
        if(!enumerated&&!found&&error!=0)throw new Win32Exception(error,"Could not inspect desktop windows (Win32 "+error+")");
        return found;
    }
    public ShopTogetherTestDesktop() {
        int failure=0;
        var creator=new Thread(()=>{
            IntPtr previous=GetThreadDesktop(GetCurrentThreadId());
            desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x01ff,IntPtr.Zero);
            if(desktop==IntPtr.Zero){failure=Marshal.GetLastWin32Error();return;}
            if(previous!=IntPtr.Zero&&!SetThreadDesktop(previous))failure=Marshal.GetLastWin32Error();
        });
        creator.Start();creator.Join();
        if(failure!=0){if(desktop!=IntPtr.Zero)CloseDesktop(desktop);desktop=IntPtr.Zero;throw new Win32Exception(failure,"Could not create isolated desktop");}
        IntPtr input=OpenInputDesktop(0,false,1);
        try{if(string.Equals(DesktopName(input),name,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Test desktop is active");}
        catch{CloseDesktop(desktop);desktop=IntPtr.Zero;throw;}
        finally{if(input!=IntPtr.Zero)CloseDesktop(input);}
    }
    public Process Start(string executable,string arguments,string workingDirectory) {
        if(desktop==IntPtr.Zero)throw new ObjectDisposedException("ShopTogetherTestDesktop");
        var startup=new StartupInfo{cb=Marshal.SizeOf(typeof(StartupInfo)),desktop=name,flags=1,show=0};
        var command=new StringBuilder("\""+executable+"\" "+arguments);ProcessInfo info;
        if(!CreateProcess(executable,command,IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,workingDirectory,ref startup,out info))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not create test process on isolated desktop");
        try {
            // Its explicit STARTUPINFO desktop is never inherited from the active desktop.
            if(!string.Equals(DesktopName(desktop),name,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Test desktop identity changed");
            IntPtr input=OpenInputDesktop(0,false,0x41);
            try{if(string.Equals(DesktopName(input),name,StringComparison.OrdinalIgnoreCase)){TerminateProcess(info.process,1);throw new InvalidOperationException("Refusing to launch on the active desktop");}}
            finally{if(input!=IntPtr.Zero)CloseDesktop(input);}
            if(ResumeThread(info.thread)==uint.MaxValue){int error=Marshal.GetLastWin32Error();TerminateProcess(info.process,1);throw new Win32Exception(error,"Could not start isolated test process");}
            // Unity can create its window on another thread. Inspect window ownership on both desktops.
            var deadline=Stopwatch.StartNew();bool isolated=false;
            while(!isolated&&deadline.Elapsed.TotalSeconds<60){
                input=OpenInputDesktop(0,false,0x41);
                try{if(HasWindow(input,info.processId))throw new InvalidOperationException("Test process created a window on the active desktop");}
                finally{if(input!=IntPtr.Zero)CloseDesktop(input);}
                isolated=HasWindow(desktop,info.processId);
                if(!isolated){if(WaitForSingleObject(info.process,0)==0)throw new InvalidOperationException("Test process exited before creating its window");Thread.Sleep(50);}
            }
            if(!isolated)throw new InvalidOperationException("Test process did not create a window on the isolated desktop");
            return Process.GetProcessById(info.processId);
        } catch {TerminateProcess(info.process,1);WaitForSingleObject(info.process,10000);throw;}
        finally {CloseHandle(info.thread);CloseHandle(info.process);}
    }
    public void PostMouse(int processId,int x,int y) {
        if(desktop==IntPtr.Zero||x<0||x>4095||y<0||y>4095)throw new InvalidOperationException("Invalid private-desktop mouse target");
        IntPtr input=OpenInputDesktop(0,false,0x41);
        try{if(string.Equals(DesktopName(input),name,StringComparison.OrdinalIgnoreCase)||HasWindow(input,processId))throw new InvalidOperationException("Refusing input on the active desktop");}
        finally{if(input!=IntPtr.Zero)CloseDesktop(input);}
        IntPtr target=IntPtr.Zero;
        EnumWindowProc callback=(window,state)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner!=(uint)processId)return true;var klass=new StringBuilder(256);GetClassName(window,klass,klass.Capacity);if(klass.ToString().IndexOf("Unity",StringComparison.OrdinalIgnoreCase)>=0){target=window;return false;}return true;};
        EnumDesktopWindows(desktop,callback,IntPtr.Zero);
        if(target==IntPtr.Zero)throw new InvalidOperationException("No Unity window on the private desktop");
        IntPtr point=(IntPtr)((y<<16)|(x&0xffff));
        if(!PostMessage(target,0x0200,UIntPtr.Zero,point)||!PostMessage(target,0x0201,(UIntPtr)1,point))throw new Win32Exception(Marshal.GetLastWin32Error(),"Private mouse press failed");
        Thread.Sleep(250);
        if(!PostMessage(target,0x0202,UIntPtr.Zero,point))throw new Win32Exception(Marshal.GetLastWin32Error(),"Private mouse release failed");
    }
    public void PostMouseAction(int processId,int x,int y,string action) {
        if(desktop==IntPtr.Zero||x<0||x>4095||y<0||y>4095)throw new InvalidOperationException("Invalid private-desktop mouse target");
        IntPtr input=OpenInputDesktop(0,false,0x41);
        try{if(string.Equals(DesktopName(input),name,StringComparison.OrdinalIgnoreCase)||HasWindow(input,processId))throw new InvalidOperationException("Refusing input on the active desktop");}
        finally{if(input!=IntPtr.Zero)CloseDesktop(input);}
        IntPtr target=IntPtr.Zero;
        EnumWindowProc callback=(window,state)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner!=(uint)processId)return true;var klass=new StringBuilder(256);GetClassName(window,klass,klass.Capacity);if(klass.ToString().IndexOf("Unity",StringComparison.OrdinalIgnoreCase)>=0){target=window;return false;}return true;};
        EnumDesktopWindows(desktop,callback,IntPtr.Zero);if(target==IntPtr.Zero)throw new InvalidOperationException("No Unity window on the private desktop");
        IntPtr point=(IntPtr)((y<<16)|(x&0xffff));uint message;UIntPtr buttons;
        if(action=="DOWN"){message=0x0201;buttons=(UIntPtr)1;}else if(action=="MOVE"){message=0x0200;buttons=(UIntPtr)1;}else if(action=="UP"){message=0x0202;buttons=UIntPtr.Zero;}else throw new InvalidOperationException("Invalid private mouse action");
        if(!PostMessage(target,message,buttons,point))throw new Win32Exception(Marshal.GetLastWin32Error(),"Private mouse action failed");
    }
    public void Dispose() { if(desktop!=IntPtr.Zero){if(!CloseDesktop(desktop))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not close isolated desktop");desktop=IntPtr.Zero;} }
}
