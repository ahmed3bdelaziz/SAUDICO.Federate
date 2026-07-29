using Autodesk.Revit.DB;using SAUDICO.Federate.Shared;namespace SAUDICO.Federate.Core.Services;
public static class ModelTypeDetector { public static ModelType Detect(string path){using var info=BasicFileInfo.Extract(path);return !info.IsWorkshared?ModelType.Local:info.IsCentral?ModelType.Central:ModelType.WorksharedLocal;} }
