using System;
using System.Windows.Automation;

class Program {
    static int Main() {
        AutomationElement root = AutomationElement.RootElement;
        var wins = root.FindAll(TreeScope.Children, Condition.TrueCondition);
        AutomationElement? window = null;
        foreach (AutomationElement w in wins) {
            if (w.Current.Name.Contains("RouterPlus") || w.Current.Name.Contains("9Router") || 
                w.Current.ProcessId == System.Diagnostics.Process.GetProcessesByName("RouterPlus")[0].Id) {
                window = w;
                break;
            }
        }
        if (window == null) {
            Console.WriteLine("ERROR: Window not found");
            return 1;
        }
        Console.WriteLine("Found: " + window.Current.Name);

        var all = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        AutomationElement? btn = null;
        foreach (AutomationElement c in all) {
            if (c.Current.Name == "Tự động thêm" && c.Current.ControlType == ControlType.Button) {
                btn = c;
                break;
            }
        }
        if (btn == null) {
            Console.WriteLine("ERROR: Button not found");
            return 1;
        }
        Console.WriteLine("Found button");
        Console.WriteLine("IsEnabled: " + btn.Current.IsEnabled);
        if (btn.Current.IsEnabled) {
            var ip = btn.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
            if (ip != null) {
                ip.Invoke();
                Console.WriteLine("Invoked");
            } else {
                Console.WriteLine("ERROR: InvokePattern not available");
                return 1;
            }
        } else {
            Console.WriteLine("Disabled");
        }
        return 0;
    }
}