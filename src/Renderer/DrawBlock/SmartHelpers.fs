﻿module SmartHelpers

open CommonTypes
open DrawHelpers
open DrawModelType
open DrawModelType.SymbolT
open DrawModelType.BusWireT
open Symbol
open BusWire
open BusWireUpdateHelpers
open SymbolHelpers

open Optics
open Operators

//-----------------------------------------------------------------------------------------------//
//---------------------------HELPERS FOR SMART DRAW BLOCK ADDITIONS------------------------------//
//-----------------------------------------------------------------------------------------------//

(*
HOW TO USE THIS MODULE.

(1) Add well-documented useful functions - see updateModelSymbols and updateModelWires
    for examples. You do not need to add performance information as in updateModelSymbols. 
    Your priority should be writing clear code. Try to avoid very inefficient implementations
    if possible (e.g. 100X slower than a similar complexity solution), but do not worry 
    about this.
(2) Note from my examples distinction between XML documentation and additional details
    in header comments.
(3) HLP23: Note comments here labelled "HLP23" which are for HLP23 class and would be deleted in
    production (Group phase) code.
(2) HLP23: Each function must have a single author specified by "HLP23: AUTHOR" in an XML comment
    as in my example: give name as Family name only (unique within teams).
(3) HLP23: Inform other members that you have written a function they could maybe use.
(4) HLP23: If two people end up with near-identical functions here team phase can rationalise if
    needed normally you are expected to share when this makes code writing faster.
(5) Note best practice here using Optics for nested record update. This is NOT curently required
    in Issie but used appropriately results in better code. Use it if you are comfortable doing so.
(5) Note on qualifying types: do this when not doing it would be ambiguous - e.g. here
    the BusWire and Symbol Model types.
(6) Note on code layout. A limit of 100 characters per line is used here. Seems about right.
*)

//----------------------------------------------------------------------------------------------//


/// Update BusWire model with given symbols. Can also be used to add new symbols.
/// This uses a fold on the Map to add symbols which makes it fast in the case that the number
/// of symbols added is very small.
let updateModelSymbols (model: BusWireT.Model) (symbols: Symbol list) : BusWireT.Model =
    // HLP23: note on fold implementation. symMap is both argument and result of the
    // fold function => sequential set of updates. In thsi case much more efficient than Map.map
    // over all symbols.
    // HLP23 - see also similar updateModelWires
    let symbols' =
        (model.Symbol.Symbols, symbols)
        ||> List.fold (fun symMap symToAdd -> Map.add symToAdd.Id symToAdd symMap)

    Optic.set (symbol_ >-> symbols_) symbols' model

/// Update BusWire model with given wires. Can also be used to add new wires.
/// This uses a fold on the Map to add wires which makes it fast in the case that the number
/// of wires added is small.
let updateModelWires (model: BusWireT.Model) (wiresToAdd: Wire list) : BusWireT.Model =
    model
    |> Optic.map wires_ (fun wireMap ->
        (wireMap, wiresToAdd)
        ||> List.fold (fun wireMap wireToAdd -> Map.add wireToAdd.WId wireToAdd wireMap))


/// Returns true if two 1D line segments intersect
/// HLP23: Derek Lai (ddl20)
let overlap1D ((a1, a2): float * float) ((b1, b2): float * float) : bool =
    let a_min, a_max = min a1 a2, max a1 a2
    let b_min, b_max = min b1 b2, max b1 b2
    a_max >= b_min && b_max >= a_min

/// Returns true if two Boxes intersect, where each box is passed in as top right and bottom left XYPos tuples
/// HLP23: Derek Lai (ddl20)
let overlap2D ((a1, a2): XYPos * XYPos) ((b1, b2): XYPos * XYPos) : bool =
    (overlap1D (a1.X, a2.X) (b1.X, b2.X)) && (overlap1D (a1.Y, a2.Y) (b1.Y, b2.Y))

/// Returns true if two Boxes intersect, where each box is passed in as a BoundingBox
/// HLP23: Derek Lai (ddl20)
let overlap2DBox (bb1: BoundingBox) (bb2: BoundingBox) : bool =
    let bb1Coords =
        { X = bb1.TopLeft.X; Y = bb1.TopLeft.Y },
        { X = bb1.TopLeft.X + bb1.W
          Y = bb1.TopLeft.Y + bb1.H }

    let bb2Coords =
        { X = bb2.TopLeft.X; Y = bb2.TopLeft.Y },
        { X = bb2.TopLeft.X + bb2.W
          Y = bb2.TopLeft.Y + bb2.H }

    overlap2D bb1Coords bb2Coords

/// Retrieves XYPos of every vertex in a wire
/// HLP23: Derek Lai (ddl20)
let getWireSegmentsXY (wire: Wire) =
    let tupToXY (l: (float * float)) : XYPos = { X = fst l; Y = snd l }

    segmentsToIssieVertices wire.Segments wire
    |> List.map (fun (x, y, _) -> (x, y))
    |> List.map tupToXY

/// Retrieves all wires which intersect an arbitrary bounding box & the index
/// of the segment which intersects the box
/// HLP23: Derek Lai (ddl20)
let getWiresInBox (box: BoundingBox) (model: Model) : (Wire * int) list =
    let wires = (List.ofSeq (Seq.cast model.Wires.Values))

    let bottomRight =
        { box.TopLeft with
            X = box.TopLeft.X + box.W
            Y = box.TopLeft.Y + box.H }

    // State Tuple - (overlapping: bool, overlapping_wire_index: int)
    let checkOverlapFolder (startPos: XYPos) (endPos: XYPos) (state: bool * int) (segment: Segment) : bool * int =
        let overlap = overlap2D (startPos, endPos) (box.TopLeft, bottomRight)
        (fst state || overlap), if overlap then segment.Index else snd state

    List.map (fun w -> foldOverNonZeroSegs checkOverlapFolder (false, -1) w, w) wires
    |> List.filter (fun l -> fst (fst l))
    |> List.map (fun ((_, index), w) -> w, index)

/// Used to fix bounding box with negative width and heights
/// HLP23: Derek Lai (ddl20)
let fixBoundingBox (box: BoundingBox): BoundingBox =
    let x = min (box.TopLeft.X + box.W) box.TopLeft.X
    let y = min (box.TopLeft.Y + box.H) box.TopLeft.Y
    {TopLeft = {X = x; Y = y}; W = abs box.W; H = abs box.H}

/// Get the start and end positions of a wire.
/// HLP23: AUTHOR Jian Fu Eng (jfe20)
let getStartAndEndWirePos (wire: Wire) : XYPos * XYPos =
    let wireVertices =
        segmentsToIssieVertices wire.Segments wire
        |> List.map (fun (x, y, _) -> { X = x; Y = y })

    let currentStartPos = wireVertices.Head
    let currentEndPos = wireVertices[wireVertices.Length - 2]

    currentStartPos, currentEndPos

/// Returns length of wire
/// HLP23: AUTHOR Jian Fu Eng (jfe20)
let getWireLength (wire: Wire) : float =
    (0., wire.Segments) ||> List.fold (fun acc seg -> acc + (abs seg.Length))

/// Gets total length of a set of wires.
/// HLP23: AUTHOR dgs119
let totalLengthOfWires (conns: Map<ConnectionId, Wire>) = 
    conns
    |> Map.map(fun _ wire -> getWireLength wire)
    |> Map.toList
    |> List.map snd
    |> List.sum

/// Checks if a wire is part of a net.
/// If yes, return the netlist. Otherwise, return None
/// HLP23: AUTHOR Jian Fu Eng (jfe20)
let isWireInNet (model: Model) (wire: Wire) : (OutputPortId * (ConnectionId * Wire) list) option =
    let nets = partitionWiresIntoNets model

    nets
    |> List.tryFind (fun (outputPortID, netlist) -> wire.OutputPort = outputPortID && netlist |> List.exists (fun (connID, w) -> connID <> wire.WId))

/// Checks if a port is part of a Symbol.
/// HLP23: AUTHOR dgs119
let isPortInSymbol (portId: string) (symbol: Symbol) : bool =
    symbol.PortMaps.Orientation |> Map.containsKey portId

/// Get pairs of unique symbols that are connected to each other.
/// HLP23: AUTHOR dgs119
let getConnSyms (wModel: BusWireT.Model) =
    wModel.Wires
    |> Map.values
    |> Seq.toList
    |> List.map (fun wire -> (getSourceSymbol wModel wire, getTargetSymbol wModel wire))
    |> List.filter (fun (symA, symB) -> symA.Id <> symB.Id)
    |> List.distinctBy (fun (symA, symB) -> Set.ofList [ symA; symB ])

/// Checks if wire is connected to two given symbols.
/// Returns false if two Symbols are the same.
/// HLP23: AUTHOR dgs119
let isConnBtwnSyms (wire: Wire) (symA: Symbol) (symB: Symbol) : bool =
    let inId, outId =
        getInputPortIdStr wire.InputPort, getOutputPortIdStr wire.OutputPort

    match inId, outId with
    | _ when (isPortInSymbol inId symA) && (isPortInSymbol outId symB) -> true
    | _ when (isPortInSymbol inId symB) && (isPortInSymbol outId symA) -> true
    | _ -> false

/// Gets connections between symbols.
/// HLP23: AUTHOR dgs119
let connsBtwnSyms (wModel: BusWireT.Model) (symA: Symbol) (symB: Symbol) : Map<ConnectionId, Wire> =
    wModel.Wires |> Map.filter (fun _ wire -> isConnBtwnSyms wire symA symB)

/// Gets Wires between symbols.
/// HLP23: AUTHOR dgs119
let wiresBtwnSyms (wModel: BusWireT.Model) (symA: Symbol) (symB: Symbol) : Wire list =
    connsBtwnSyms wModel symA symB |> Map.toList |> List.map snd

/// Filters Ports by Symbol.
/// HLP23: AUTHOR dgs119
let filterPortBySym (ports: Port list) (sym: Symbol) =
    ports |> List.filter (fun port -> ComponentId port.HostId = sym.Id)

/// Gets Ports From a List of Wires.
/// HLP23: AUTHOR dgs119
let portsOfWires (model: BusWireT.Model) (wires: Wire list) =
    wires
    |> List.map (fun wire ->
        [ getPort model.Symbol (getInputPortIdStr wire.InputPort)
          getPort model.Symbol (getOutputPortIdStr wire.OutputPort) ])
    |> List.concat
    |> List.distinct

/// Groups Wires by the net they belong to.
/// HLP23: AUTHOR dgs119
let groupWiresByNet (conns: Map<ConnectionId, Wire>) =
    conns
    |> Map.toList
    |> List.groupBy (fun (_, wire) -> wire.OutputPort)
    |> List.map (snd >> List.map snd)

/// Scales a symbol so it has the provided height and width.
/// HLP23: AUTHOR BRYAN TAN
let setCustomCompHW (h: float) (w: float) (sym: Symbol) =
    let hScale = w / sym.Component.W
    let vScale = h / sym.Component.H

    { sym with
        HScale = Some hScale
        VScale = Some vScale }

/// For a wire and a symbol, return the edge of the symbol that the wire is connected to.
/// /// HLP23: AUTHOR BRYAN TAN
let wireSymEdge wModel wire sym =
    let sPort, tPort = getSourcePort wModel wire, getTargetPort wModel wire
    let sEdge = Map.tryFind sPort.Id sym.PortMaps.Orientation
    let tEdge = Map.tryFind tPort.Id sym.PortMaps.Orientation

    match sEdge, tEdge with
    | Some e, None -> e
    | None, Some e -> e
    | _ -> Top // Shouldn't happen.




//-------------------------------------------------------------------------------------------------//
//------------------------TYPES USED INTERNALLY FOR SEPARATION AND ORDERING------------------------//
//-------------------------------------------------------------------------------------------------//

module Constants =
    let wireSeparationFromSymbol = 7. // must be smaller than Buswire.nubLength
    let maxCallsToShiftHorizontalSeg = 5
    /// Must be smaller than Buswire.nubLength
    let minWireSeparation = 7.
    let smallOffset = 0.0001
    let minSegmentSeparation = 12.
    let maxSegmentSeparation = 15.
    /// lines within this distance of each other are considered to overlap
    let overlapTolerance = 2.
    /// corners with max length edge large rthan this are not removed
    let maxCornerSize = 100.
    /// How close are segment extensions caused by corner removal allowed to
    /// get to other elements? Maybe needs to be smaller than some otehr things for
    /// successful corner removal?
    let extensionTolerance = 3.

type DirectionToMove =
    | Up_
    | Down_
    | Left_
    | Right_

let swapXY (pos: XYPos) (orientation: Orientation) : XYPos =
    match orientation with
    | Horizontal -> pos
    | Vertical -> { X = pos.Y; Y = pos.X }

let swapBB (box: BoundingBox) (orientation: Orientation) : BoundingBox =
    match orientation with
    | Horizontal -> box
    | Vertical -> { 
            TopLeft = swapXY box.TopLeft orientation
            W = box.H
            H = box.W }

let updatePos (direction: DirectionToMove) (distanceToShift: float) (pos: XYPos) : XYPos =
    match direction with
    | Up_ -> { pos with Y = pos.Y - distanceToShift }
    | Down_ -> { pos with Y = pos.Y + distanceToShift }
    | Left_ -> { pos with X = pos.X - distanceToShift }
    | Right_ -> { pos with X = pos.X + distanceToShift }


/// Used to capture the 1D coordinates of the two ends of a line. (see Line).
type Bound = { MinB: float; MaxB: float }

    
type LineId = LineId of int
with member this.Index = match this with | LineId i -> i

type LType = 
    /// a non-segment fixed (symbol boundary) barrier
    | FIXED  
    /// a movable line segment
    | NORMSEG 
    /// a segment which is a fixed barrier in clustering but can change after.
    | FIXEDSEG 
    /// a fixed segment which has been manually routed and can never move
    | FIXEDMANUALSEG
    /// a segment linked to another on the same net which is not clustered
    | LINKEDSEG


/// Used to represent a line on the canvas, e.g. a wire segment or symbol edge.
/// The array of lines will all have the same orientation - so optimisation is done in two phases
/// for vertical and horizontal segments.
type Line =
    { 
        P: float // the coordinate X or Y perpendicular to the line.
        B: Bound // the two "other" coordinates
        Orientation: Orientation
        Seg1: ASegment option // if the line comes from a wire segment this references the segment and wire
        LType: LType
        SameNetLink: Line list
        Wid: ConnectionId
        PortId: OutputPortId
        Lid: LineId } // index in lines array of this Line.


/// Used to cluster together overlapping and adjacent lines into a group that
/// can be spread out. This is the parameter in a tail recursion used to do the clustering
type Cluster =
    { 
        UpperFix: float option // if clustering is stopped by a barrier
        LowerFix: float option // if clustering is stopped by a barrier
        Segments: int list // list of movable lines found (which will be spread out)
        Bound: Bound } // union of bounds of all segments found so far

/// Controls direction of Cluster search in expandCluster.
/// Search is upwards first and then downwards so downwards search takes a Cluster
/// (generated from upwards search) as parameter.
type LocSearchDir =
    | Upwards
    | Downwards of Cluster

type Extension = {    
    ExtOri: Orientation
    ExtB: Bound
    ExtP: float
    }

/// Defines a wire corner that could be removed.
/// the two removed segments are [StartSeg+1..EndSeg-1]
/// In addition, StartSeg and EndSeg have Length changed.
type WireCorner = {
    /// Wire on which the corner lies
    Wire: Wire
    /// index of segment immediately before the two deleted segments
    StartSeg: int
    /// change in length of StartSeg needed to remove the corner
    StartSegChange: float
    /// change in length of EndSeg needed to remove the corner
    EndSegChange: float // EndSeg = StartSeg + 3
    /// orientation of StartSeg. EndSeg has opposite orientation.
    StartSegOrientation: Orientation
    }

type LineInfo = {
    /// Vertical lines
    VLines: Line array
    /// Horizontal lines
    HLines: Line array
    /// map from wire IDs to wires
    WireMap: Map<ConnectionId,Wire>
    /// map from segment IDs to lines
    LineMap: Map<int*ConnectionId, LineId>}


//-------------------------------------------------------------------------------------------------//
//--------------------------------HELPERS USED IN CLUSTERING SEGMENTS------------------------------//
//-------------------------------------------------------------------------------------------------//
open Constants
/// get the horizontal length of the visible segment emerging from a port
let getVisibleNubLength (atEnd: bool) (wire: Wire) =
    let segs = wire.Segments
    let getLength i =
        if atEnd then
            segs.Length - 1 - i
        else
            i
        |> (fun index -> segs[index].Length)
    if (getLength 1) < smallOffset then
        getLength 2 + getLength 0
    else
        getLength 0

let segmentIsNubExtension (wire: Wire) (segIndex: int) =
    let segs = wire.Segments
    let nSegs = segs.Length
    let lastSeg = nSegs-1
    let revSeg n = segs[lastSeg-n]
    match segIndex, lastSeg-segIndex with
    | 0, _ | _, 0 -> true
    | 2, _ when segs[1].IsZero() -> true
    |_, 2 when  (revSeg 1).IsZero() -> true
    | _ -> false
        
                

/// Get the segment indexes within a Cluster (loc)
let inline segPL (lines: Line array) loc =
    loc.Segments |> (List.map (fun n -> lines[n].P))

/// ideal (max) width of segments in loc
let inline widthS (loc: Cluster) =
    float loc.Segments.Length * maxSegmentSeparation

/// ideal upper bound in P direction of segments with P value in pts.
let inline upperS pts =
    (List.min pts + List.max pts) / 2.
    + float pts.Length * maxSegmentSeparation / 2.

/// ideal lower bound in P direction of segments with P value in pts
let inline lowerS pts =
    (List.min pts + List.max pts) / 2.
    - float pts.Length * maxSegmentSeparation / 2.

/// ideal upper bound in P direction of loc including possible fixed constraint
let inline upperB (lines: Line array) (loc: Cluster) =
    let pts = segPL lines loc

    match loc.UpperFix, loc.LowerFix with
    | Some u, _ -> u
    | None, Some l when l > lowerS pts -> l + widthS loc
    | _ -> upperS pts

/// ideal lower bound in P direction of loc including possible fixed constraint
let inline lowerB (lines: Line array) loc =
    let pts = segPL lines loc

    match loc.UpperFix, loc.LowerFix with
    | _, Some l -> l
    | Some u, None when u < upperS pts -> u - widthS loc
    | _ -> lowerS pts

//-------------------------------------------------------------------------------------------------//
//--------------------------------LOW-LEVEL PRINTING (returns strings)-----------------------------//
//-------------------------------------------------------------------------------------------------//

let pWire (wire: Wire) =
    let segs = wire.Segments
    let nSegs = segs.Length
    let aSegs = getAbsSegments wire
    let pASeg (aSeg:ASegment) =
        let vec = aSeg.End - aSeg.Start
        if aSeg.IsZero() then
            "S0"
        else
            match getSegmentOrientation aSeg.Start aSeg.End, aSeg.Segment.Length > 0 with
            | Vertical, true -> "Dn"
            | Vertical, false -> "Up"
            | Horizontal,true -> "Rt"
            | Horizontal, false -> "Lt"
            |> (fun s -> s + $"%.0f{abs aSeg.Segment.Length}")

    let pSegs = aSegs |> List.map pASeg |> String.concat "-"

    sprintf $"W{nSegs}:{wire.InitialOrientation}->{pSegs}"

let pOpt (x: 'a option) = match x with | None -> "None" | Some x -> $"^{x}^"

let pLineType (line:Line) = $"{line.LType}"

let pLine (line:Line) = 
    let ori = match line.Orientation with | Horizontal -> "H" | Vertical -> "V"
    $"|{ori}L{line.Lid.Index}.P=%.0f{line.P}.{pLineType line}:B=%.0f{line.B.MinB}-%.0f{line.B.MaxB}|"

let pLines (lineA: Line array) =
    $"""{lineA |> Array.map (fun line -> pLine line) |> String.concat "\n"}"""

let pCluster (loc:Cluster) =
    $"Cluster:<{pOpt loc.LowerFix}-{loc.Segments}-{pOpt loc.UpperFix}>"

let pAllCluster (lines: Line array) (loc:Cluster) =
    let oris = match lines[0].Orientation with | Horizontal -> "Horiz" | Vertical -> "Vert"
    $"""Cluster-{oris}:<L={pOpt loc.LowerFix}-{loc.Segments |> List.map (fun n -> pLine lines[n]) |> String.concat ","}-U={pOpt loc.UpperFix}>"""


//-------------------------------------------------------------------------------------------------//
//-----------------------------------------UTILITY FUNCTIONS---------------------------------------//
//-------------------------------------------------------------------------------------------------//

let rec tryFindIndexInArray (searchStart: LineId) (dir: int) (predicate: 'T -> bool) (giveUp: 'T -> bool) (arr: 'T array) =
    if searchStart.Index < 0 || searchStart.Index > arr.Length - 1 then
        None
    else
        match predicate arr[searchStart.Index], giveUp arr[searchStart.Index] with
        | _, true -> None
        | true, _ -> Some searchStart
        | false, _ -> tryFindIndexInArray (LineId(searchStart.Index + dir)) dir predicate giveUp arr
            

/// true if bounds b1 and b2 overlap or are exactly adjacent
let hasOverlap (b1: Bound) (b2: Bound) =
    inMiddleOrEndOf b1.MinB b2.MinB b1.MaxB
    || inMiddleOrEndOf b1.MinB b2.MaxB b1.MinB
    || inMiddleOrEndOf b2.MinB b1.MinB b2.MaxB

/// true if bounds b1 and b2 overlap or are exactly adjacent
let hasNearOverlap (tolerance: float) (b1: Bound) (b2: Bound) =
    inMiddleOf (b1.MinB-tolerance) b2.MinB (b1.MaxB+tolerance)
    || inMiddleOf (b1.MinB-tolerance)  b2.MaxB (b1.MinB+tolerance)
    || inMiddleOf (b2.MinB-tolerance) b1.MinB (b2.MaxB+tolerance)

/// Union of two bounds b1 and b2. b1 & b2 must overlap or be adjacent,
/// otherwise the inclusive interval containing b1 and b2 is returned.
let boundUnion (b1: Bound) (b2: Bound) =
    {   MinB = min b1.MinB b2.MinB
        MaxB = max b1.MaxB b2.MaxB }


/// Move segment by amount posDelta in direction perpendicular to segment - + => X or y increases.
/// movement is by changing lengths of two segments on either side.
/// will fail if called to change a nub at either end of a wire (nubs cannot move).
let moveSegment (index: int) (posDelta: float) (wire: Wire) =
    let segs = wire.Segments

    if index < 1 || index > segs.Length - 2 then
        failwithf $"What? trying to move segment {index} of a wire length {segs.Length}"

    { wire with
        Segments =
            segs
            |> List.updateAt (index - 1) { segs[index - 1] with Length = segs[index - 1].Length + posDelta }
            |> List.updateAt (index + 1) { segs[index + 1] with Length = segs[index + 1].Length - posDelta } }

/// Change wires to move a wire segment represented by line to the given new value of P coordinate.
/// P is X or Y according to ori.
let moveLine (ori: Orientation) (newP: float) (line: Line) (wires: Map<ConnectionId, Wire>) =
    match line.Seg1 with
    | None -> failwithf "Can't move Line {line} - it is not a segment"
    | Some seg ->
        let oldP =
            match ori with
            | Horizontal -> seg.Start.Y
            | Vertical -> seg.Start.X

        let segIndex = seg.Segment.Index
        let wid = seg.Segment.WireId
        let updateWire = Option.map (moveSegment segIndex (newP - oldP))
        Map.change seg.Segment.WireId updateWire wires