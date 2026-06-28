using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace PLBORDER
{
    public class PlBorderClass
    {
        [CommandMethod("PLBORDER")]
        public void CreatePolylineBorder()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions plEntity = new PromptEntityOptions("\nSelect polyline: ");
            plEntity.SetRejectMessage("\nOnly Polyline is supported.");
            plEntity.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult plObj = ed.GetEntity(plEntity);

            if (plObj.Status != PromptStatus.OK) return;

            ObjectId pplId = ObjectId.Null;
            ObjectId nplId = ObjectId.Null;
            ObjectId startLineId = ObjectId.Null;
            ObjectId endLineId = ObjectId.Null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pl = tr.GetObject(plObj.ObjectId, OpenMode.ForRead) as Polyline;
                if (pl == null)
                {
                    ed.WriteMessage("\nSelected object is not a polyline.");
                    return;
                }

                double plWidth = pl.ConstantWidth;
                if (plWidth <= 0)
                {
                    ed.WriteMessage("\nPolyline does not have a constant width. Please provide global width for the selected polyline in its properties.");
                    return;
                }

                double offsetDistance = plWidth / 2.0;

                DBObjectCollection positiveOffset = pl.GetOffsetCurves(offsetDistance);
                DBObjectCollection negativeOffset = pl.GetOffsetCurves(-offsetDistance);

                if (positiveOffset.Count == 0 || negativeOffset.Count == 0)
                {
                    ed.WriteMessage("\nFailed while creating offset curves. Please ensure the polyline is valid and has a constant width.");
                    return;
                }

                Polyline positivePolyline = positiveOffset[0] as Polyline;
                Polyline negativePolyline = negativeOffset[0] as Polyline;

                if (positivePolyline == null || negativePolyline == null)
                {
                    ed.WriteMessage("\nFailed while creating offset polylines.");
                    return;
                }

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                positivePolyline.LayerId = pl.LayerId;
                negativePolyline.LayerId = pl.LayerId;

                pplId = btr.AppendEntity(positivePolyline);
                tr.AddNewlyCreatedDBObject(positivePolyline, true);
                nplId = btr.AppendEntity(negativePolyline);
                tr.AddNewlyCreatedDBObject(negativePolyline, true);

                Line startConnectingLine = new Line(positivePolyline.StartPoint, negativePolyline.StartPoint);
                startConnectingLine.LayerId = pl.LayerId;
                startLineId = btr.AppendEntity(startConnectingLine);
                tr.AddNewlyCreatedDBObject(startConnectingLine, true);

                Line endConnectingLine = new Line(positivePolyline.EndPoint, negativePolyline.EndPoint);
                endConnectingLine.LayerId = pl.LayerId;
                endLineId = btr.AppendEntity(endConnectingLine);
                tr.AddNewlyCreatedDBObject(endConnectingLine, true);

                Entity[] entities_to_join = { negativePolyline, startConnectingLine, endConnectingLine };

                IntegerCollection joined = positivePolyline.JoinEntities(entities_to_join);

                if(joined.Count != entities_to_join.Length)
                {
                    ed.WriteMessage("\nFailed to join the border lines. Stepped back the processes! Please ensure the polyline is valid and has a constant width.");
                    return;
                }

                positivePolyline.Closed = true;
                positivePolyline.LayerId = pl.LayerId;
                positivePolyline.Color = pl.Color;
                positivePolyline.ConstantWidth = 0.0;

                negativePolyline.Erase();
                startConnectingLine.Erase();
                endConnectingLine.Erase();

                tr.Commit();
            }
        }
    }
}