using System.Collections.Generic;
using UnityEngine;

namespace OctoMapSharp
{
    /// <summary>
    /// The publicly accessible octomap node data structure containing the position and size of a node
    /// </summary>
    public struct OctoMapNodeRaw
    {
        /// <summary>
        /// The position in 3D space of this node
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// The size of all the sides of this nodes
        /// </summary>
        public float Size;

        /// <summary>
        /// Basic constructor that initializes the position and size of the data structure
        /// </summary>
        /// <param name="position">The value to initialize the nodes position with</param>
        /// <param name="size">The value to initialize the nodes size with</param>
        public OctoMapNodeRaw(Vector3 position, float size)
        {
            Position = position;
            Size = size;
        }
    }

    public class OctoMap
    {
        /// <summary>
        /// A node within the OctoMap which can contain a pointer to an array of child nodes.
        /// Can also contain additional per-node information.
        /// </summary>
        private struct OctoMapNode
        {
            /// <summary>
            /// The ID of the dictionary entry in '_nodeChildren' which contains an array of indexes to this nodes children.
            /// </summary>
            public uint? ChildArrayId { get; set; }

            /// <summary>
            /// The most basic way of representing a nodes occupancy: binary. -1 is unoccupied, 0 is unknown, 1 is occupied.
            /// </summary>
            public int Occupied { get; set; }
        }

        /// <summary>
        /// The origin position of the OctoMap. Individual node positions are derived from this root position.
        /// </summary>
        private Vector3 _rootNodePosition;

        /// <summary>
        /// The current size of the root node which can increase if data is added.
        /// </summary>
        private float _rootNodeSize;

        /// <summary>
        /// The minimum size a node can get when traversing through the OctoMap.
        /// </summary>
        private readonly float _minimumNodeSize;

        /// <summary>
        /// Each node in the OctoMap along with an unique index accessor.
        /// </summary>
        private readonly Dictionary<uint, OctoMapNode> _nodes;

        /// <summary>
        /// Nodes with children have an entry in this dictionary with the value containing an array of node IDs.
        /// </summary>
        private readonly Dictionary<uint, uint[]> _nodeChildren;

        /// <summary>
        /// The accessor index for the root node.
        /// </summary>
        private uint _rootNodeId;

        /// <summary>
        /// The current highest index in the nodes dictionary, used to keep every index unique.
        /// This solution works because potential duplicate indexes will only occur when we get near to
        /// the max uint value, but long before this happens the memory consumption will be the big issue.
        /// </summary>
        private uint _nodeHighestIndex;

        /// <summary>
        /// The current highest index in the node children dictionary, used to keep every index unique.
        /// </summary>
        private uint _nodeChildrenHighestIndex;

        /// <summary>
        /// Initialise an empty OctoMap. 
        /// </summary>
        /// <param name="startingRootNodePosition">The starting position of the root node of the OctoMap.</param>
        /// <param name="startingRootNodeSize">The starting size of the root node of the OctoMap.</param>
        /// <param name="minimumNodeSize">The minimum size of a node.</param>
        public OctoMap(Vector3 startingRootNodePosition, float startingRootNodeSize, float minimumNodeSize)
        {
            _nodes = new Dictionary<uint, OctoMapNode>();
            _nodeChildren = new Dictionary<uint, uint[]>();

            _rootNodePosition = startingRootNodePosition;
            _rootNodeSize = startingRootNodeSize;
            _minimumNodeSize = minimumNodeSize;

            _rootNodeId = _nodeHighestIndex++;
            _nodes[_rootNodeId] = new OctoMapNode();
        }

        /// <summary>
        /// Initialise an OctoMap from a compact bitstream.
        /// </summary>
        /// <param name="startingRootNodePosition">The starting position of the root node of the OctoMap.</param>
        /// <param name="startingRootNodeSize">The starting size of the root node of the OctoMap.</param>
        /// <param name="minimumNodeSize">The minimum size of a node.</param>
        /// <param name="octomapBitstream">The compact bitstream containing the OctoMap parent-child relationships.</param>
        public OctoMap(Vector3 startingRootNodePosition, float startingRootNodeSize, float minimumNodeSize,
            byte[] octomapBitstream)
        {
            _nodes = new Dictionary<uint, OctoMapNode>();
            _nodeChildren = new Dictionary<uint, uint[]>();

            _rootNodePosition = startingRootNodePosition;
            _rootNodeSize = startingRootNodeSize;
            _minimumNodeSize = minimumNodeSize;

            BitStream bitStream = new BitStream(octomapBitstream);

            OctoMapNode rootNode = new OctoMapNode();
            _rootNodeId = _nodeHighestIndex++;
            _nodes.Add(_rootNodeId, rootNode);

            BuildOctoMapFromBitStreamRecursive(bitStream, _rootNodeId);
        }

        #region Compact bit stream

        /// <summary>
        /// Converts the OctoMap in memory to a compact bitstream data structure.
        /// </summary>
        /// <returns>The compact bitstream.</returns>
        public BitStream ConvertToBitStream()
        {
            // Number of bytes will be number of child node arrays multiplied by 2
            // (each item in the array is a node with children and each node takes up 2 bytes)
            int streamLength = _nodeChildren.Count * 2;

            BitStream bitStream = new BitStream(new byte[streamLength]);
            ConvertToBitStreamRecursive(bitStream, _rootNodeId);
            return bitStream;
        }

        /// <summary>
        /// Recursive function that traverses through node children and writes the relationships to a compact bit stream.
        /// </summary>
        /// <param name="bitStream">The bitstream to write to.</param>
        /// <param name="currentNodeId">The node ID the current recursive traversal is on.</param>
        private void ConvertToBitStreamRecursive(BitStream bitStream, uint currentNodeId)
        {
            OctoMapNode currentNode = _nodes[currentNodeId];
            if (currentNode.ChildArrayId != null)
            {
                // Build up the current nodes bit stream based on status of its children
                for (int i = 0; i < 8; i++)
                {
                    OctoMapNode childNode = _nodes[_nodeChildren[currentNode.ChildArrayId.Value][i]];
                    if (childNode.ChildArrayId != null) // INNER NODE
                    {
                        bitStream.WriteBit(1);
                        bitStream.WriteBit(1);
                    }
                    else if (CheckNodeFree(childNode)) // FREE
                    {
                        bitStream.WriteBit(1);
                        bitStream.WriteBit(0);
                    }
                    else if (CheckNodeOccupied(childNode)) // OCCUPIED
                    {
                        bitStream.WriteBit(0);
                        bitStream.WriteBit(1);
                    }
                    else // UNKNOWN
                    {
                        bitStream.WriteBit(0);
                        bitStream.WriteBit(0);
                    }
                }

                for (int i = 0; i < 8; i++)
                {
                    uint childId = _nodeChildren[currentNode.ChildArrayId.Value][i];
                    ConvertToBitStreamRecursive(bitStream, childId);
                }
            }
        }

        /// <summary>
        /// Builds the node and child nodes dictionaries by traversing through the compact bitstream.
        /// </summary>
        /// <param name="bitStream">The bitstream to traverse</param>
        /// <param name="currentNodeId">The node ID the current recursive traversal is on.</param>
        private void BuildOctoMapFromBitStreamRecursive(BitStream bitStream, uint currentNodeId)
        {
            // Create child nodes of this current node
            uint[] childIdArray = new uint[8];
            for (int i = 0; i < 8; i++)
            {
                uint childNodeId = _nodeHighestIndex++;
                _nodes.Add(childNodeId, new OctoMapNode());
                childIdArray[i] = childNodeId;
            }

            // Add child ids to dictionary
            uint childArrayId = _nodeChildrenHighestIndex++;
            _nodeChildren.Add(childArrayId, childIdArray);

            // Create node and add child array linkage
            OctoMapNode currentNode = _nodes[currentNodeId];
            currentNode.ChildArrayId = childArrayId;
            _nodes[currentNodeId] = currentNode;

            // Correctly set the state of each of the children created
            List<uint> innerNodeChildren = new List<uint>();
            for (int i = 0; i < 8; i++)
            {
                int firstBit = bitStream.ReadBit().AsInt();
                int secondBit = bitStream.ReadBit().AsInt();

                OctoMapNode childNode;
                if (firstBit == 1 && secondBit == 1) // INNER NODE
                {
                    innerNodeChildren.Add(childIdArray[i]);
                }
                else if (firstBit == 0 && secondBit == 1) // OCCUPIED
                {
                    childNode = _nodes[childIdArray[i]];
                    _nodes[childIdArray[i]] = IncreaseNodeOccupation(childNode);
                }
                else if (firstBit == 1 && secondBit == 0) // FREE
                {
                    childNode = _nodes[childIdArray[i]];
                    _nodes[childIdArray[i]] = DecreaseNodeOccupation(childNode);
                }

                // else UNKNOWN
            }

            // Now loop through each child that is an inner node
            for (int i = 0; i < innerNodeChildren.Count; i++)
            {
                BuildOctoMapFromBitStreamRecursive(bitStream, innerNodeChildren[i]);
            }
        }

        #endregion

        #region Query OctoMap

        /// <summary>
        /// Check if a Ray intersects any nodes in the OctoMap and returns the smallest node that it intersects.
        /// </summary>
        /// <param name="ray">The ray that will be used in the intersection query.</param>
        /// <returns>A nullable vector3 that is the centre of the node that it hit (if it hit anything).</returns>
        public Vector3? GetRayIntersection(Ray ray)
        {
            return GetRayIntersectionRecursive(ref ray, _rootNodeSize, _rootNodePosition, _rootNodeId);
        }

        /// <summary>
        /// Recursive function that traverses through nodes in order to check for a ray intersection.
        /// </summary>
        /// <param name="ray">The ray that will be used in the intersection query.</param>
        /// <param name="currentNodeSize">The node size the current recursive traversal is on.</param>
        /// <param name="currentNodeCentre">The node centre the current recursive traversal is on.</param>
        /// <param name="currentNodeId">The node ID the current recursive traversal is on.</param>
        /// <returns></returns>
        private Vector3? GetRayIntersectionRecursive(ref Ray ray, float currentNodeSize, Vector3 currentNodeCentre,
            uint currentNodeId)
        {
            // Check if the ray intersects the current nodes bounds
            Bounds bounds = new Bounds(currentNodeCentre,
                new Vector3(currentNodeSize, currentNodeSize, currentNodeSize));
            if (!bounds.IntersectRay(ray))
            {
                return null;
            }

            // If the ray intersects the current node, check if the current node is occupied
            OctoMapNode currentNode = _nodes[currentNodeId];
            if (CheckNodeOccupied(currentNode))
            {
                return currentNodeCentre;
            }

            // If the ray intersects the current node but the node is not occupied, check its children if it has any
            if (currentNode.ChildArrayId != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    uint childId = _nodeChildren[currentNode.ChildArrayId.Value][i];
                    float newNodeSize = currentNodeSize / 2;
                    Vector3 newNodeCentre = GetBestFitChildNodeCentre(i, newNodeSize, currentNodeCentre);

                    Vector3? intersectedNodeCentre =
                        GetRayIntersectionRecursive(ref ray, newNodeSize, newNodeCentre, childId);
                    if (intersectedNodeCentre != null)
                    {
                        return intersectedNodeCentre;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Contribute To The Octomap

        /// <summary>
        /// Adds a point to the OctoMap which will mark a specific node as occupied.
        /// </summary>
        /// <param name="point">The 3D point to add to the OctoMap.</param>
        public void AddPoint(Vector3 point)
        {
            int growCount = 0;
            while (true)
            {
                // Check if the root node encompasses this point. If it does, start the recursive adding process
                Bounds bounds = new Bounds(_rootNodePosition, new Vector3(_rootNodeSize, _rootNodeSize, _rootNodeSize));
                if (bounds.Contains(point))
                {
                    AddPointRecursive(ref point, _rootNodeSize, _rootNodePosition, _rootNodeId);
                    return;
                }

                // If the root node doesn't encompass the point, grow the OctoMap
                GrowOctomap(point - _rootNodePosition);
                growCount++;

                if (growCount > 20)
                {
                    Debug.Log("Aborted add operation as it seemed to be going on forever (" + (growCount - 1) +
                              ") attempts at growing the OctoMap.");
                    return;
                }
            }
        }

        /// <summary>
        /// The recursive function that adds a new point to the OctoMap.
        /// </summary>
        /// <param name="point">The point to add to the OctoMap.</param>
        /// <param name="currentNodeSize">The node size the current recursive traversal is on.</param>
        /// <param name="currentNodeCentre">The node centre the current recursive traversal is on.</param>
        /// <param name="currentNodeId">The node ID the current recursive traversal is on.</param>
        private void AddPointRecursive(ref Vector3 point, float currentNodeSize, Vector3 currentNodeCentre,
            uint currentNodeId)
        {
            OctoMapNode node = _nodes[currentNodeId];

            // If we're at the deepest level possible, this current node becomes a leaf node.
            if (currentNodeSize < _minimumNodeSize)
            {
                _nodes[currentNodeId] = IncreaseNodeOccupation(node);
                return;
            }

            // If the node doesn't encompass this point, return out early
            Bounds bounds = new Bounds(currentNodeCentre,
                new Vector3(currentNodeSize, currentNodeSize, currentNodeSize));
            if (!bounds.Contains(point))
                return;

            // If this node doesn't have any children, new children need to be generated
            if (node.ChildArrayId == null)
            {
                node.ChildArrayId = GenerateChildren();
                _nodes[currentNodeId] = node;
            }

            Debug.Assert(_nodes[currentNodeId].ChildArrayId != null);

            // Now handle the new object we're adding now
            int bestFitChild = BestFitChildIndex(point, currentNodeCentre);
            uint childNodeId = _nodeChildren[node.ChildArrayId.Value][bestFitChild];

            float newNodeSize = currentNodeSize / 2;
            Vector3 newNodeCentre = GetBestFitChildNodeCentre(bestFitChild, newNodeSize, currentNodeCentre);

            AddPointRecursive(ref point, newNodeSize, newNodeCentre, childNodeId);

            // Loop through all children and check if they all share the same occupied state
            bool childrenOccupancy = false;
            for (int i = 0; i < 8; i++)
            {
                OctoMapNode child = _nodes[_nodeChildren[node.ChildArrayId.Value][i]];
                if (child.ChildArrayId != null)
                {
                    return;
                }

                bool occupancy = CheckNodeOccupied(child);

                if (i == 0)
                {
                    childrenOccupancy = occupancy;
                }
                else
                {
                    if (occupancy != childrenOccupancy)
                    {
                        return;
                    }
                }
            }

            // If the code reaches here that means all the children share the same state
            // Remove all the child nodes
            for (int i = 0; i < 8; i++)
            {
                var childArrayId = _nodes[currentNodeId].ChildArrayId;
                if (childArrayId != null)
                {
                    uint childId = _nodeChildren[childArrayId.Value][i];
                    _nodes.Remove(childId);
                }
                else
                {
                    Debug.Log("Failed to remove node from node dictionary");
                }
            }

            var arrayId = _nodes[currentNodeId].ChildArrayId;
            if (arrayId != null)
            {
                // Remove the node children array
                _nodeChildren.Remove(arrayId.Value);
            }
            else
            {
                Debug.Log("Failed to remove child array from child dictionary.");
            }

            // Set the current nodes occupancy state to that of its children
            OctoMapNode currentNode = _nodes[currentNodeId];
            currentNode.ChildArrayId = null;
            currentNode = childrenOccupancy ? IncreaseNodeOccupation(currentNode) : DecreaseNodeOccupation(currentNode);

            _nodes[currentNodeId] = currentNode;
        }

        /// <summary>
        /// Adds a ray to the OctoMap which will set any nodes along its path to free.
        /// </summary>
        /// <param name="originPoint">The origin of the ray</param>
        /// <param name="recordedPoint">The 3D point that the ray has hit</param>
        public void AddRayToOctoMap(Vector3 originPoint, Vector3 recordedPoint)
        {
            Ray ray = new Ray(originPoint, (recordedPoint - originPoint).normalized);
            AddRayToOctoMap(ray, recordedPoint, _rootNodeSize, _rootNodePosition, _rootNodeId);
        }

        /// <summary>
        /// The recursive function that adds any intersecting nodes along this ray to the octomap
        /// </summary>
        /// <param name="ray">The ray that will be used to check intersections against</param>
        /// <param name="recordedPoint">The recorded point that the ray hit</param>
        /// <param name="currentNodeSize">The node size the current recursive traversal is on.</param>
        /// <param name="currentNodeCentre">The node centre the current recursive traversal is on.</param>
        /// <param name="currentNodeId">The node ID the current recursive traversal is on.</param>
        private void AddRayToOctoMap(Ray ray, Vector3 recordedPoint, float currentNodeSize, Vector3 currentNodeCentre,
            uint currentNodeId)
        {
            OctoMapNode currentNode = _nodes[currentNodeId];

            // If this node is a leaf node
            if (currentNodeSize < _minimumNodeSize)
            {
                // Do not check this ray against the node that shares the same centre as the registered point
                if (recordedPoint == currentNodeCentre)
                {
                    return;
                }

                // Mark this leaf node as free
                _nodes[currentNodeId] = DecreaseNodeOccupation(currentNode);
                return;
            }

            // Generate some children if the current node doesn't have any
            if (currentNode.ChildArrayId == null)
            {
                currentNode.ChildArrayId = GenerateChildren();
                _nodes[currentNodeId] = currentNode;
            }

            // Recursively call this function on each of this current nodes children
            for (int i = 0; i < 8; i++)
            {
                uint childId = _nodeChildren[currentNode.ChildArrayId.Value][i];
                float newNodeSize = currentNodeSize / 2;
                Vector3 newNodeCentre = GetBestFitChildNodeCentre(i, newNodeSize, currentNodeCentre);

                Bounds bounds = new Bounds(newNodeCentre, new Vector3(newNodeSize, newNodeSize, newNodeSize));
                if (bounds.IntersectRay(ray))
                {
                    AddRayToOctoMap(ray, recordedPoint, newNodeSize, newNodeCentre, childId);
                }
            }
        }

        #endregion

        #region Return Nodes

        /// <summary>
        /// Returns a list of the octomap leaf nodes with each node having a position and size
        /// </summary>
        /// <returns>The list of octomap nodes</returns>
        public List<OctoMapNodeRaw> GetOctoMapNodes()
        {
            List<OctoMapNodeRaw> nodes = new List<OctoMapNodeRaw>();
            GatherOctoMapNodes(nodes, _rootNodeSize, _rootNodePosition, _rootNodeId);
            return nodes;
        }

        /// <summary>
        /// Recursively traverses though the octomap in order to find leaf nodes and then adds them to a list
        /// </summary>
        /// <param name="nodes">The list of nodes</param>
        /// <param name="currentNodeSize">The node size the current recursive traversal is on.</param>
        /// <param name="currentNodeCentre">The node centre the current recursive traversal is on.</param>
        /// <param name="currentNodeId">The node ID the current recursive traversal is on.</param>
        private void GatherOctoMapNodes(List<OctoMapNodeRaw> nodes, float currentNodeSize, Vector3 currentNodeCentre,
            uint currentNodeId)
        {
            OctoMapNode currentNode = _nodes[currentNodeId];
            if (CheckNodeOccupied(currentNode))
            {
                nodes.Add(new OctoMapNodeRaw(currentNodeCentre, currentNodeSize));
            }

            if (currentNode.ChildArrayId != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    uint childId = _nodeChildren[currentNode.ChildArrayId.Value][i];
                    float newNodeSize = currentNodeSize / 2;
                    Vector3 newNodeCentre = GetBestFitChildNodeCentre(i, newNodeSize, currentNodeCentre);
                    GatherOctoMapNodes(nodes, newNodeSize, newNodeCentre, childId);
                }
            }
        }

        #endregion

        #region Node Evaluation

        /// <summary>
        /// Sets the occupied state of a node to 1. Can be extended further.
        /// </summary>
        /// <param name="octoMapNode">The node to set the occupied state on</param>
        /// <returns>The same node but with the occupied state changed</returns>
        private static OctoMapNode IncreaseNodeOccupation(OctoMapNode octoMapNode)
        {
            octoMapNode.Occupied = 1;
            return octoMapNode;
        }

        /// <summary>
        /// Sets the occupied state of a node to -1. Can be extended further.
        /// </summary>
        /// <param name="octoMapNode">The node to set the occupied state on</param>
        /// <returns>The same node but with the occupied state changed</returns>
        private static OctoMapNode DecreaseNodeOccupation(OctoMapNode octoMapNode)
        {
            octoMapNode.Occupied = -1;
            return octoMapNode;
        }

        /// <summary>
        /// Returns true or false depending on whether a specific node is occupied. Can be extended further.
        /// </summary>
        /// <param name="octoMapNode">The node that will be checked</param>
        /// <returns>True or false depending on whether a specific node is occupied.</returns>
        private static bool CheckNodeOccupied(OctoMapNode octoMapNode)
        {
            return octoMapNode.Occupied >= 1;
        }

        /// <summary>
        /// Returns true or false depending on whether a specific node is not occupied. Can be extended further.
        /// </summary>
        /// <param name="octoMapNode">The node that will be checked</param>
        /// <returns>True or false depending on whether a specific node is not occupied.</returns>
        private static bool CheckNodeFree(OctoMapNode octoMapNode)
        {
            return octoMapNode.Occupied <= -1;
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Expand the root node to encompass any additional added points by specifying the direction that it needs
        /// </summary>
        /// <param name="direction">The direction that the OctoMap needs to grow in</param>
        private void GrowOctomap(Vector3 direction)
        {
            int xDirection = direction.x >= 0 ? 1 : -1;
            int yDirection = direction.y >= 0 ? 1 : -1;
            int zDirection = direction.z >= 0 ? 1 : -1;

            uint oldRoot = _rootNodeId;
            float half = _rootNodeSize / 2;

            _rootNodeSize = _rootNodeSize * 2;
            _rootNodePosition =
                _rootNodePosition + new Vector3(xDirection * half, yDirection * half, zDirection * half);

            // Create a new, bigger octomap root node
            _rootNodeId = _nodeHighestIndex++;
            _nodes.Add(_rootNodeId, new OctoMapNode());

            // Create 7 new octomap children to go with the old root as children of the new root
            int rootPos = GetRootPosIndex(xDirection, yDirection, zDirection);
            uint[] childIds = new uint[8];
            for (int i = 0; i < 8; i++)
            {
                if (i == rootPos)
                {
                    childIds[i] = oldRoot;
                }
                else
                {
                    uint childNodeId = _nodeHighestIndex++;
                    _nodes.Add(childNodeId, new OctoMapNode());
                    childIds[i] = childNodeId;
                }
            }

            // Add child ids to dict
            uint childId = _nodeChildrenHighestIndex++;
            _nodeChildren.Add(childId, childIds);

            // Attach the new children to the new root node
            OctoMapNode node = _nodes[_rootNodeId];
            node.ChildArrayId = childId;
            _nodes[_rootNodeId] = node;
        }

        /// <summary>
        /// This function generates 8 new nodes, adds them to the dictionaries, and returns the accessor index of item
        /// </summary>
        /// <returns>The accessor index of the new node child dictionary item</returns>
        private uint GenerateChildren()
        {
            uint[] childIdArray = new uint[8];
            for (int i = 0; i < 8; i++)
            {
                uint childNodeId = _nodeHighestIndex++;
                _nodes.Add(childNodeId, new OctoMapNode());
                childIdArray[i] = childNodeId;
            }

            uint childArrayId = _nodeChildrenHighestIndex++;
            _nodeChildren.Add(childArrayId, childIdArray); // Add child ids
            return childArrayId;
        }

        /// <summary>
        /// Used when growing the octomap. Works out where the old root node would fit inside a new, larger root node.
        /// </summary>
        /// <param name="xDir">The XDir that the child is in</param>
        /// <param name="yDir">The YDir that the child is in</param>
        /// <param name="zDir">The ZDir that the child is in</param>
        /// <returns>The array index that this child in this direction has</returns>
        private static int GetRootPosIndex(int xDir, int yDir, int zDir)
        {
            int result = xDir > 0 ? 1 : 0;
            if (yDir < 0) result += 4;
            if (zDir > 0) result += 2;
            return result;
        }

        /// <summary>
        /// Returns the index of the child that is the best fit for a point within a specific node
        /// </summary>
        /// <param name="point">The point that will be checked</param>
        /// <param name="currentNodeCentre">The node that the point will be checked against</param>
        /// <returns>The index that of the child</returns>
        private static int BestFitChildIndex(Vector3 point, Vector3 currentNodeCentre)
        {
            return (point.x <= currentNodeCentre.x ? 0 : 1) + (point.y >= currentNodeCentre.y ? 0 : 4) +
                   (point.z <= currentNodeCentre.z ? 0 : 2);
        }

        /// <summary>
        /// Returns the position of a child of a specific node at a specific index
        /// </summary>
        /// <param name="childIndex">The child index of the node</param>
        /// <param name="childSize">The size of the child node</param>
        /// <param name="parentPosition">The position of the parent node</param>
        /// <returns>The position of the child</returns>
        private static Vector3 GetBestFitChildNodeCentre(int childIndex, float childSize, Vector3 parentPosition)
        {
            float quarter = childSize / 4f;

            switch (childIndex)
            {
                case 0:
                    return parentPosition + new Vector3(-quarter, quarter, -quarter);
                case 1:
                    return parentPosition + new Vector3(quarter, quarter, -quarter);
                case 2:
                    return parentPosition + new Vector3(-quarter, quarter, quarter);
                case 3:
                    return parentPosition + new Vector3(quarter, quarter, quarter);
                case 4:
                    return parentPosition + new Vector3(-quarter, -quarter, -quarter);
                case 5:
                    return parentPosition + new Vector3(quarter, -quarter, -quarter);
                case 6:
                    return parentPosition + new Vector3(-quarter, -quarter, quarter);
                case 7:
                    return parentPosition + new Vector3(quarter, -quarter, quarter);
            }

            Debug.Log("Failed to determine best fit child node centre.");
            return Vector3.zero;
        }

        #endregion
    }
}