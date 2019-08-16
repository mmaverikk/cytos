﻿using MathNet.Spatial.Euclidean;
using SharedComponents.XML;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace MSystemSimulationEngine.Classes.Xml
{
    //TODO low priority: rewrite to XDocument

    /// <summary>
    /// Serializes simulation snapshots to Xml structure.
    /// </summary>
    public class SerializeSnapshot
    {
        #region Private data

        /// <summary>
        /// Xml document containing Snapshot file.
        /// </summary>
        private readonly XDocument v_XmlDoc;

        private readonly XElement v_RootXmlElement;

        #endregion

        #region Constructor

        /// <summary>
        /// Snapshots serializer constructor.         
        /// Creates new Xml document with the master snapshots element.
        /// </summary>
        /// <param name="MSystemPath">Path to MSystem description</param>
        public SerializeSnapshot(string MSystemPath)
        {

            v_XmlDoc = new XDocument();
            v_XmlDoc.Add(new XElement("root",
                new XElement("MSystemDescription"),
                new XElement("snapshots")));

            v_RootXmlElement = v_XmlDoc.Root;

            // TODO change all colors to the format aarrggbb
            XDocument mSystemDescription = XDocument.Load(MSystemPath);
            XElement tiling = mSystemDescription.Root.GetElement("tiling");
            XElement Msystem = mSystemDescription.Root.GetElement("Msystem");

            v_RootXmlElement.GetElement("MSystemDescription").Add(tiling, Msystem);

        }

        #endregion

        #region Private methods

        /// <summary>
        /// Serializes floating objects.
        /// </summary>
        /// <param name="floatingObjectsNode">Xml node where the floating objects belong.</param>
        /// <param name="floatingObjects">Floating objects to serialize.</param>
        private void SerializeFloatingObjects(XElement floatingObjectsNode, FloatingObjectsSet floatingObjects)
        {
            foreach (FloatingObjectInSpace floatingObject in floatingObjects)
            {
                /*  <floatingObject name="a" objectID="1">
                        <position>
                            <posX value="0"/>
                            <posY value="0"/>
                            <posZ value="0"/>
                        </position>
                    </floatingObject>  */

                XElement floatingObjectElement =
                    new XElement("floatingObject",
                        new XAttribute("name", floatingObject.Name),
                        new XAttribute("objectID", floatingObject.ID.ToString()));
                SerializePosition(floatingObjectElement, floatingObject.Position);

                floatingObjectsNode.Add(floatingObjectElement);
            }
        }


        /// <summary>
        /// Serializes tiles.
        /// </summary>
        /// <param name="tilesNode">Xml node where the tiles belong.</param>
        /// <param name="tiles">Tiles to serialize.</param>
        private void SerializeTiles(XElement tilesNode, IEnumerable<TileInSpace> tiles)
        {
            foreach (TileInSpace tile in tiles)
            {
                if (tile.State != TileInSpace.FState.Unchanged)
                {
                    SerializeTile(tilesNode,
                        tile.Name,
                        tile.ID,
                        tile.Vertices is Segment3D ? "rod" : "tile",
                        tile.State,
                        tile.Vertices,
                        tile.Color,
                        tile.Alpha);
                }
                //Change color of tile in space
                else if (tile.State == TileInSpace.FState.Unchanged && tile.ColorWasChanged)
                {
                    //Destroy old one
                    SerializeTile(tilesNode,
                        tile.Name,
                        tile.ID,
                        tile.Vertices is Segment3D ? "rod" : "tile",
                        TileInSpace.FState.Destroy,
                        tile.Vertices,
                        tile.Color,
                        tile.Alpha);
                    //Create new one with new color
                    SerializeTile(tilesNode,
                        tile.Name,
                        tile.ID,
                        tile.Vertices is Segment3D ? "rod" : "tile",
                        TileInSpace.FState.Create,
                        tile.Vertices,
                        tile.Color,
                        tile.Alpha);
                }

                // If any connector was disconnected and it still stick to its previous tile, we emphasize its color
                uint count = 0;
                foreach (var connector in tile.Connectors)
                {
                    count++;
                    // Should the connector be displayed in an emphasized color?
                    bool emphasize = connector.Positions.Count == 2 && // TODO GENERALIZE ALSO FOR POINT CONNECTORS
                                     connector.ConnectedTo == null &&
                                     connector.OnTile.ID < connector.WasConnectedTo?.OnTile.ID &&
                                     // Only one of the two overlapping connectors will emphasize the edge
                                     connector.WasConnectedTo?.OnTile.State != TileInSpace.FState.Destroy &&
                                     connector.Overlaps(connector.WasConnectedTo);

                    var state = TileInSpace.FState.Unchanged;

                    if (connector.WasEmphasized)
                    {
                        if (!emphasize)
                            state = TileInSpace.FState.Destroy;
                        else if (tile.State == TileInSpace.FState.Destroy || tile.State == TileInSpace.FState.Move)
                            state = tile.State;
                    }
                    else if (emphasize && tile.State != TileInSpace.FState.Destroy)
                        state = TileInSpace.FState.Create;

                    connector.WasEmphasized = emphasize;

                    if (state != TileInSpace.FState.Unchanged)
                    {
                        // Store a fake tile shaped as the conector to the snapshot file
                        SerializeTile(tilesNode,
                            tile.Name + "- connector " + connector.Name,
                            tile.ID * 1000000 + count,
                            "rod",
                            state,
                            connector.Positions,
                            connector.EmphColor,
                            255,
                            0.1);

                    }
                }
            }
        }


        /// <summary>
        /// Serializes single tile.
        /// </summary>
        /// <param name="tilesNode">Xml node where the tile belong.</param>
        /// <param name="name">Tile name</param>
        /// <param name="id">Tile unique ID</param>
        /// <param name="type">Tile type (rod, tile)</param>
        /// <param name="state">Tile state (Move, Create, Destroy)</param>
        /// <param name="vertices">Tile vertices</param>
        /// <param name="color">Tile color</param>
        /// <param name="alpha">Alpha attribute of the tile color</param>
        /// <param name="thickness">Tile thickness - relevant only for rods, ignored if <=0</param>
        private void SerializeTile(XElement tilesNode, string name, ulong id, string type, TileInSpace.FState state, IEnumerable<Point3D> vertices, Color color, int alpha, double thickness = 0)
        {
            /*  <tile name="b" objectID="1" type="tile" state="create">
                    <vertices>
                        <vertex>
                            <posX value="0"/>s
                            <posY value="0"/>
                            <posZ value="0"/>
                        </vertex>
                        ...
                    </vertice>
                    <color name = "4000bfff">
                    </color>
            </tile>  */

            XElement tileElement =
                new XElement("tile",
                    new XAttribute("name", name),
                    new XAttribute("objectID", id.ToString()),
                    new XAttribute("type", type),
                    new XAttribute("state", state.ToString()));

            XElement verticesNode = new XElement("vertices");
            tileElement.Add(verticesNode);

            foreach (Point3D vertex in vertices)
                SerializePosition(verticesNode, vertex, "vertex");
            SerializeColor(tileElement, color, alpha);      // Serialize in ARGB, no name
            if (thickness > 0)
            {
                tileElement.Add(new XElement("thickness", new XAttribute("value", thickness.ToString(CultureInfo.InvariantCulture))));
            }
            tilesNode.Add(tileElement);
        }


        /// <summary>
        /// Serializes 3D position of an object.
        /// </summary>
        /// <param name="objectNode">Xml node where the position is added.</param>
        /// <param name="position">Position in 3D.</param>
        /// <param name="nodeName">Name of the created Xml node</param>
        private void SerializePosition(XElement objectNode, Point3D position, string nodeName = "position")
        {
            XElement positionNode =
                new XElement(nodeName,
                    new XElement("posX", new XAttribute("value", position.X.ToString(CultureInfo.InvariantCulture))),
                    new XElement("posY", new XAttribute("value", position.Y.ToString(CultureInfo.InvariantCulture))),
                    new XElement("posZ", new XAttribute("value", position.Z.ToString(CultureInfo.InvariantCulture))));
            objectNode.Add(positionNode);
        }

        /// <summary>
        /// Serializes 3D angle of an object.
        /// </summary>
        /// <param name="objectNode">Xml node where the angle is added.</param>
        /// <param name="angle">Angle in 3D.</param>
        private void SerializeAngle(XElement objectNode, EulerAngles angle)
        {
            // Quaternion.ToEulerAngles rotates Z-Y-X, while Unity requires Z-X-Y
            XElement angleNode =
                new XElement("angle",
                    new XElement("angleX", new XAttribute("value", angle.Alpha.Degrees.ToString(CultureInfo.InvariantCulture))),
                    new XElement("angleY", new XAttribute("value", angle.Beta.Degrees.ToString(CultureInfo.InvariantCulture))),
                    new XElement("angleZ", new XAttribute("value", angle.Gamma.Degrees.ToString(CultureInfo.InvariantCulture))));

            objectNode.Add(angleNode);
        }

        /// <summary>
        /// Serializes color of an object.
        /// </summary>
        /// <param name="objectNode">Xml node where the color is added.</param>
        /// <param name="color">Color</param>
        /// <param name="alpha">Alpha attribute of the color</param>
        private void SerializeColor(XElement objectNode, Color color, int alpha)
        {
            // We need to ensure that the displayed color is ARGB in hex, not the color name
            XElement colorElement = new XElement("color", new XAttribute("name", Color.FromArgb(alpha, color).Name.PadLeft(8, '0')));
            objectNode.Add(colorElement);
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Gets Xml document as a string.
        /// </summary>
        /// <returns></returns>
        public string GetXmlDocAsAString()
        {
            return v_XmlDoc.ToString();
        }

        /// <summary>
        /// Saves XML document as XML file.
        /// </summary>
        /// <param name="path">String location where you want to save the file.</param>
        public void SaveXmlFile(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            v_XmlDoc.Save(path);
        }

        /// <summary>
        /// Serializes one snapshot.
        /// </summary>
        /// <param name="stepID">Snapshot number.</param>
        /// <param name="floatingObjects">Floating objects to be serialized.</param>
        /// <param name="tiles">Tiles to be serialized.</param>
        public void Serialize(ulong stepID, FloatingObjectsSet floatingObjects, IEnumerable<TileInSpace> tiles)
        {
            // Child snapshot declaration.
            XElement snapshot = new XElement("snapshot", new XAttribute("stepID", stepID.ToString()));
            v_RootXmlElement.GetElement("snapshots").Add(snapshot);

            // Child floatingObjects.
            XElement floatingObjectsNode = new XElement("floatingObjects");
            snapshot.Add(floatingObjectsNode);

            // Child tiles.
            XElement tilesNode = new XElement("tiles");
            snapshot.Add(tilesNode);

            SerializeFloatingObjects(floatingObjectsNode, floatingObjects);
            SerializeTiles(tilesNode, tiles);
        }

        #endregion

    }
}