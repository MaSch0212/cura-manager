using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CuraManager.Legacy.CuraAutomation;

public class CuraProjectNameAutomation : ICuraProjectNameAutomation
{
    public void SetProjectName(Process curaProcess, string executableFileName, string projectName)
    {
        if (executableFileName == "Cura")
            SetName4x(curaProcess, projectName);
        else
            SetName5x(curaProcess, projectName);
    }

    // Body moved verbatim from CuraService.OpenCura's local function SetName4x.
    // `p` becomes the `process` parameter and `printName` becomes `projectName`.
    private static void SetName4x(Process process, string projectName)
    {
        if (
            TryFindChild(
                AutomationElement.RootElement,
                CheckWindow,
                TimeSpan.FromMinutes(5),
                out var curaWindow
            )
            && TryFindChild(
                curaWindow,
                CheckEditNameButton,
                TimeSpan.FromSeconds(30),
                out var editNameButton
            )
            && editNameButton.TryGetCurrentPattern(InvokePattern.Pattern, out var objInvokePattern)
            && objInvokePattern is InvokePattern invokePattern
        )
        {
            invokePattern.Invoke();
            SendKeys.SendWait($"{projectName}{{ENTER}}");
        }

        bool CheckWindow(TreeWalker treeWalker, AutomationElement e)
        {
            return e.Current.ProcessId == process.Id
                && e.Current.Name?.Contains("Ultimaker Cura") == true;
        }

        bool CheckEditNameButton(TreeWalker treeWalker, AutomationElement e)
        {
            return ReferenceEquals(e.Current.ControlType, ControlType.Button)
                && string.IsNullOrEmpty(e.Current.Name)
                && !e.Current.IsOffscreen
                && ReferenceEquals(
                    treeWalker.GetNextSibling(e)?.Current.ControlType,
                    ControlType.Edit
                );
        }
    }

    // Body moved verbatim from CuraService.OpenCura's local function SetName5x.
    private static void SetName5x(Process process, string projectName)
    {
        if (
            TryFindChild(
                AutomationElement.RootElement,
                CheckWindow,
                TimeSpan.FromMinutes(5),
                out var curaWindow
            )
            && TryFindChild(
                curaWindow,
                CheckEditNameButton,
                TimeSpan.FromSeconds(30),
                out var editNameButton
            )
            && editNameButton.TryGetCurrentPattern(ValuePattern.Pattern, out var objValuePattern)
            && objValuePattern is ValuePattern valuePattern
        )
        {
            valuePattern.SetValue(projectName);
        }

        bool CheckWindow(TreeWalker treeWalker, AutomationElement e)
        {
            return e.Current.ProcessId == process.Id
                && e.Current.Name?.Contains("Ultimaker Cura", StringComparison.OrdinalIgnoreCase)
                    == true;
        }

        bool CheckEditNameButton(TreeWalker treeWalker, AutomationElement e)
        {
            return ReferenceEquals(e.Current.ControlType, ControlType.Edit)
                && !e.Current.IsOffscreen
                && e.TryGetCurrentPattern(ValuePattern.Pattern, out var objValuePattern)
                && objValuePattern is ValuePattern valuePattern
                && !valuePattern.Current.IsReadOnly;
        }
    }

    // Moved verbatim from CuraService.TryFindChild, except that MaSch's Waiter.WaitUntil
    // is replaced by the local Poll helper below to keep this assembly dependency-free.
    private static bool TryFindChild(
        AutomationElement parent,
        Func<TreeWalker, AutomationElement, bool> checkFunc,
        TimeSpan timeout,
        out AutomationElement element
    )
    {
        var treeWalker = TreeWalker.RawViewWalker;
        element = Poll(
            () =>
            {
                var e = treeWalker.GetFirstChild(parent);
                while (e != null && !checkFunc(treeWalker, e))
                    e = treeWalker.GetNextSibling(e);
                return e;
            },
            timeout
        );
        return element != null;
    }

    /// <summary>
    /// Replaces MaSch <c>Waiter.WaitUntil</c> with <c>ThrowException = false</c>: polls until
    /// the factory returns non-null or the timeout elapses, then returns null.
    /// </summary>
    private static AutomationElement Poll(Func<AutomationElement> factory, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            var element = factory();
            if (element != null)
                return element;
            Thread.Sleep(250);
        } while (stopwatch.Elapsed < timeout);

        return null;
    }
}
