using RoBIM.Manager.Core;
internal static class DisplayWordingTests
{
 public static void Run(Action<string,Action> test)
 {
  void Require(bool value){if(!value)throw new Exception("Display wording contract failed");}
  test("LegacyClassificationRemovedFromToolDescription",()=>Require(ToolNames.Description("자동 결합 · 독립 Add-in")=="자동 결합" && ToolNames.Description("Revit 독립 Add-in 호스트 로더")=="Revit Add-in 호스트 로더"));
  test("LegacySheetRuntimePrefixLosesClassificationOnly",()=>Require(ToolNames.Description("Revit 2026.5+ / .NET 10 독립 Add-in · DWG/PDF/OCR 시트 생성")=="Revit 2026.5+ / .NET 10 Add-in · DWG/PDF/OCR 시트 생성"));
  test("IndependentViewMeaningIsPreserved",()=>Require(ToolNames.Description("종속 뷰가 없는 독립 뷰의 회전")=="종속 뷰가 없는 독립 뷰의 회전"));
  test("LegacyGroupChangesOnlyPresentation",()=>Require(ToolNames.Group("RoBIM / 독립 도구 TEST")=="RoBIM / 도구" && ToolNames.Group("RoBIM 관리 / 독립 Add-in")=="RoBIM 관리 / 관리"));
  test("DisplayWordingIsIdempotent",()=>{var once=ToolNames.Description("시트 생성 · 독립 Add-in");Require(ToolNames.Description(once)==once && ToolNames.Display("독립 Add-in Reload")=="Reload");});
 }
}
