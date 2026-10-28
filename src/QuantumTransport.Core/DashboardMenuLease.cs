namespace QuantumTransport.Core;

// Owns only the UI resources this dashboard acquired. Clear ownership before
// callbacks so repeated close, modal cancellation and scene cleanup are safe.
public sealed class DashboardMenuLease
{
    private Action? _dismiss, _resume, _restore;
    public bool Active { get; private set; }
    public void Acquire(Action? resumeOwnedMenu, Action? restoreExistingSelection)
    {
        if(Active) throw new InvalidOperationException("Dashboard menu is already acquired");
        _resume=resumeOwnedMenu; _restore=restoreExistingSelection; Active=true;
    }
    public void OwnModal(Action dismiss)
    {
        if(!Active) throw new InvalidOperationException("Dashboard menu has not been acquired");
        _dismiss=dismiss;
    }
    public void Release()
    {
        if(!Active) return;
        var dismiss=_dismiss; var resume=_resume; var restore=_restore;
        Active=false; _dismiss=null; _resume=null; _restore=null;
        List<Exception> errors=[];
        try { dismiss?.Invoke(); } catch(Exception error) {errors.Add(error);}
        try { if(resume!=null) resume(); else restore?.Invoke(); } catch(Exception error) {errors.Add(error);}
        if(errors.Count>0) throw new AggregateException("Dashboard UI cleanup failed",errors);
    }
}

public enum DashboardCommand { Close, Overview, Machines, Rules, PreviousPage, NextPage, ToggleTransport }
public static class DashboardActions
{
    public static bool TryParse(string action, out DashboardCommand command)
    {
        command=action switch
        {
            "close"=>DashboardCommand.Close, "tab:0"=>DashboardCommand.Overview,
            "tab:1"=>DashboardCommand.Machines, "tab:2"=>DashboardCommand.Rules,
            "prev"=>DashboardCommand.PreviousPage, "next"=>DashboardCommand.NextPage,
            "toggle_transport"=>DashboardCommand.ToggleTransport, _=>(DashboardCommand)(-1)
        };
        return Enum.IsDefined(command);
    }
}
