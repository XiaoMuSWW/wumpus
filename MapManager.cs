using Godot;
using System;
using System.Collections.Generic;

[Tool]
[GlobalClass]
public partial class MapManager : Node2D
{
	[Export] private ulong seed;
	private ulong initial_state;
	[Export] private int mapSize = 8;
	[Export] private int gold_num = 1;[Export] private int hole_num = 3;
	
	[Export] private TileMapLayer baseGroundLayer;
	[Export] private TileMapLayer GoldLayer;
	[Export] private TileMapLayer WumpusLayer;

	[Signal]
	public delegate void screamEventHandler(bool isScream);
	
	public enum GridType { Normal = 0, Hole = 1, Gold = 2, Wumpus = 3, None = 4 }
	
	private RandomNumberGenerator _rng = new RandomNumberGenerator();
	private HashSet<Vector2I> safeZone = new HashSet<Vector2I>(); // 存储必定安全连通路径点

	public override void _Ready()
	{
		_rng.Seed = seed;
		initial_state = _rng.State;
		reGenerate();
	}

	public GridType GetGridType(Vector2I pos)
	{	
		// 阻挡边界查询
		if (pos.X < 0 || pos.Y < 0 || pos.X >= mapSize || pos.Y >= mapSize) return GridType.None;

		if (WumpusLayer.GetCellSourceId(pos) == 0) return GridType.Wumpus;
		if (GoldLayer.GetCellSourceId(pos) == 0) return GridType.Gold;
		if (baseGroundLayer.GetCellSourceId(pos) == 0) return GridType.Normal;
		else if (baseGroundLayer.GetCellSourceId(pos) == 1) return GridType.Hole;
		
		return GridType.None;
	}
	
	public void generate_base_ground()
	{
		baseGroundLayer.Clear();
		for(int i = 0; i < mapSize; ++i) {
			for(int j = 0; j < mapSize; ++j) {
				baseGroundLayer.SetCell(new Vector2I(i,j), 0, Vector2I.Zero);
			}		
		}
	}
	
	public void generate_random_gold()
	{	
		GoldLayer.Clear();
		safeZone.Clear();
		safeZone.Add(Vector2I.Zero); // 保证起点安全无怪无坑
	
		for(int i = 0; i < gold_num; ++i)
		{
			int x = _rng.RandiRange(0, mapSize-1);
			int y = _rng.RandiRange(0, mapSize-1);
			if (x == 0 && y == 0) continue;
			
			Vector2I goldPos = new Vector2I(x,y);
			GoldLayer.SetCell(goldPos, 0, Vector2I.Zero);

			// ==================
			// 构建绝对有解的安全路径
			// ==================
			int cx = 0, cy = 0;
			while (cx != x || cy != y)
			{
				if (cx != x && cy != y) {
					if (_rng.Randf() > 0.5f) cx += (x > cx ? 1 : -1);
					else cy += (y > cy ? 1 : -1);
				}
				else if (cx != x) cx += (x > cx ? 1 : -1);
				else cy += (y > cy ? 1 : -1);
				
				safeZone.Add(new Vector2I(cx, cy));
			}
		}
	}
	
	public void generate_hole()
	{
		for(int i = 0; i < hole_num; ++i)
		{
			int x = _rng.RandiRange(0, mapSize-1);
			int y = _rng.RandiRange(0, mapSize-1);
			Vector2I pos = new Vector2I(x, y);

			// 避开安全区与金子
			if (safeZone.Contains(pos)) continue;
			if (GoldLayer.GetCellTileData(pos) != null) continue;
			
			baseGroundLayer.SetCell(pos, 1, new Vector2I(0,0));
		}
	}

	public void generate_wumpus()
	{
		WumpusLayer.Clear();
		int attempts = 0;
		while (attempts < 100)
		{
			int x = _rng.RandiRange(0, mapSize-1);
			int y = _rng.RandiRange(0, mapSize-1);
			Vector2I pos = new Vector2I(x, y);

			if (safeZone.Contains(pos)) { attempts++; continue; }
			
			if (baseGroundLayer.GetCellSourceId(pos) == 0) {
				WumpusLayer.SetCell(pos, 0, new Vector2I(0,0));
				break;	
			}
			attempts++;
		}
	}

	public void onDigAt(Vector2I pos)
	{
		if(GetGridType(pos) == GridType.Gold) {
			GoldLayer.EraseCell(pos);
		}
	}

	public void onShotTo(Vector2I pos, Vector2I direction)
	{
		var arrowPos = pos;
		while(GetGridType(arrowPos) != GridType.None) {
			if(GetGridType(arrowPos) == GridType.Wumpus) {
				WumpusLayer.EraseCell(arrowPos);
				EmitSignal(SignalName.scream, true);
				break;
			}
			// GD.Print("Arrow", arrowPos);
			arrowPos += direction;
		}
		
		if (GetGridType(arrowPos) == GridType.None) {
			arrowPos -= direction;
			EmitSignal(SignalName.scream, false);
		}
		Vector2I swapD = new Vector2I(direction.Y, direction.X);
		List<Vector2I> wumpu_poses =[arrowPos - direction, arrowPos + direction, arrowPos + swapD, arrowPos - swapD];
		foreach(Vector2I p in wumpu_poses) wumpu_startled(p);
	}
	
	public void wumpu_startled(Vector2I pos)
	{
		if(GetGridType(pos) != GridType.Wumpus) return;
		// GD.Print("wupus startled!");
		Godot.Collections.Array<Vector2I> around =[Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right];
		Godot.Collections.Array<Vector2I> canMovTo =[];
		foreach(Vector2I d in around) {
			var wumpus_pos = pos + d;
			if(GetGridType(wumpus_pos) == GridType.Gold || GetGridType(wumpus_pos) == GridType.Normal) {
				canMovTo.Add(wumpus_pos);
			}
		}
		if (canMovTo.Count > 0) {
			var index = _rng.RandiRange(0, canMovTo.Count-1);
			WumpusLayer.EraseCell(pos);
			WumpusLayer.SetCell(canMovTo[index], 0, Vector2I.Zero);
		}
	}

	public void reGenerate()
	{
		_rng.State = initial_state;
		Clear();
		generate_base_ground();
		generate_random_gold();
		generate_hole();
		generate_wumpus();
	}

	private void Clear()
	{
		foreach(var p in baseGroundLayer.GetUsedCells()) baseGroundLayer.EraseCell(p);
		foreach(var p in GoldLayer.GetUsedCells()) GoldLayer.EraseCell(p);
		foreach(var p in WumpusLayer.GetUsedCells()) WumpusLayer.EraseCell(p);
	}
}