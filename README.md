# OctoMapSharp
An Efficient Probabilistic 3D Mapping Framework Based on Octrees. A Unity C# port of the original C++ implementation.

# Acknowledgements
- OctoMap white paper and C++ implementation originally developed by Kai M. Wurm and Armin Hornung. [LINK](https://github.com/OctoMap/octomap)
- BitStream stream wrapper to read/write bits and other data types developed by Rubendal. [LINK](https://github.com/rubendal/BitStream)

# Requirements
- Unity Scripting Runtime Version .NET 4.x Equivalent

# Features
- Create an OctoMap data structure by defining a starting position and size as well as the minimum node size.
- Add 3D points to the OctoMap and mark nodes as occupied. Recursive subdivision is used to mark the leaf node (as defined by the minimum node size) that encompasses the added point as occupied.
- Mark nodes along a ray as free. Recursive subdivision is used to find leaf nodes that the ray intersects as marks them as free. 
- Child nodes are pruned (removed) if they share the same occupancy state and the parent node's occupancy value is set to it.
- Compact bitstream serialization (as first defined in the white paper) that reduces the OctoMap to a small size by only storing parent-child relationships as opposed to individual node positions.