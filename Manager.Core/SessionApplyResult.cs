namespace RoBIM.Manager.Core;
public sealed record SessionApplyResult(int Pid,long StartTicks,string Status,string? Message=null)
{
    public static string Summary(string host,IReadOnlyList<SessionApplyResult> values) =>
        host+" 적용 완료 "+values.Count(v=>v.Status=="applied")+"개 / 대기 "+values.Count(v=>v.Status is not ("applied" or "failed"))+"개 / 실패 "+values.Count(v=>v.Status=="failed")+"개 · "+
        string.Join(", ",values.Select(v=>"PID "+v.Pid+": "+(v.Status=="applied"?"완료":v.Status=="failed"?"실패"+(string.IsNullOrWhiteSpace(v.Message)?"":" ("+v.Message+")"):"대기")));
}
