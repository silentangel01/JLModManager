namespace JLModManager.Core;

public record WorkProgress(string Stage, long Done, long Total);
public sealed class WorkScope : IDisposable
{
    static readonly AsyncLocal<WorkScope?> local=new();
    readonly WorkScope? previous;
    public static WorkScope? Current=>local.Value;
    public CancellationToken Token { get; }
    readonly IProgress<WorkProgress>? progress;
    long lastReport;
    public WorkScope(CancellationToken token,IProgress<WorkProgress>? progress=null) { previous=local.Value;local.Value=this;Token=token;this.progress=progress; }
    public static void Check()=>Current?.Token.ThrowIfCancellationRequested();
    public static void Report(string stage,long done=0,long total=0) { Check();var current=Current;if(current==null)return;long now=Environment.TickCount64;if(now-current.lastReport<100&&done!=total)return;current.lastReport=now;current.progress?.Report(new(stage,done,total)); }
    public void Dispose()=>local.Value=previous;
    public static void CopyStream(Stream input,Stream output,long length,string stage)
    {
        var buffer=new byte[81920];long done=0;int n;
        while(true) { Check();n=input.Read(buffer);if(n==0)break;done+=n;if(done>length)throw new InvalidDataException("文件长度不匹配。");output.Write(buffer,0,n);Report(stage,done,length); }
        if(done!=length)throw new InvalidDataException("文件不完整。");
    }
}
