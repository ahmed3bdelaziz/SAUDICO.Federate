using System;using System.Threading;using Autodesk.Revit.DB;using Autodesk.Revit.ApplicationServices;using SAUDICO.Federate.Core.Models;namespace SAUDICO.Federate.Core.Interfaces;
public interface IDocumentOpener { Document Open(ModelInfo model,Application app); }
public interface ITemporaryViewBuilder { View3D Create(Document doc); }
public interface INwcExporter { ExportResult Export(Document doc,View3D view,FederationJob job); }
public interface IJobProcessor { ExportResult Process(FederationJob job,Application app,IProgress<string>? progress,CancellationToken token); }
public interface IRevitRequestQueue { void Enqueue(RevitRequest request); }
public sealed class RevitRequest { public FederationJob Job{get;}public CancellationToken Token{get;}public Action<FederationJob,string> Progress{get;}public Action<ExportResult> Complete{get;}public Action<Exception> Fail{get;}public RevitRequest(FederationJob j,CancellationToken t,Action<FederationJob,string> p,Action<ExportResult> c,Action<Exception> f){Job=j;Token=t;Progress=p;Complete=c;Fail=f;} }
