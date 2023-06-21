using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DropMod
{
    public class DropManager : Manager<DropManager>
    {
        // from octdat
        [OctSaveIgnoreField]
        public float attemptsPerDay = 2;

        // from octdat
        [OctSaveIgnoreField]
        public int dropLimit = 3;

        // from octdat
        [OctSaveIgnoreField]
        public float dropDecayDays = 3;

        // from octdat
        [field: OctSaveIgnoreField]
        public List<DropType> types { get; private set; } = new List<DropType>();
        public void AddType(DropType type)
        {
            types.Add(type);
        }

        // instances in the game
        public List<DropActor> drops { get; private set; } = new List<DropActor>();
        public void AddDrop(DropActor drop)
        {
            drops.Add(drop);
            RefreshBehavior(drop);
        }
        public void RemoveDrop(DropActor drop)
        {
            drops.Remove(drop);
        }

        // prefab instances - not saved, regenerated on load
        public List<DropBehavior> dropBehs { get; private set; } = new List<DropBehavior>();
        public void AddDropBehavior(DropBehavior dropBeh)
        {
            dropBehs.Add(dropBeh);
        }
        public void RemoveDropBehavior(DropBehavior dropBeh)
        {
            dropBehs.Remove(dropBeh);
        }

        // time of next drop
        public TimeStamp nextDrop;

        // PostLoad is called when a savegame is finished loading
        // add support when added to an existing game, refresh behaviors
        public override void PostLoad()
        {
            base.PostLoad();

            if (nextDrop.IsZero())
            {
                nextDrop = TimeManager.Instance.seconds + TimeManager.SecondsPerDay / (attemptsPerDay * OctoberMath.DistributedRandom(.5f, 2f));
            }

            RefreshVisible();
        }

        // closemap is called when we're done playing in a map
        public override void CloseMap()
        {
            for (int i = drops.Count - 1; i >= 0; i = Mathf.Min(i, drops.Count) - 1)
            {
                if (drops[i].behavior != null)
                {
                    GameObject.Destroy(drops[i].dropBeh);
                    drops[i].SetBehavior(null);
                }
            }

            base.CloseMap();
        }

        // openmap is called when a map becomes the current playing area
        public override void OpenMap()
        {
            base.OpenMap();

            RefreshVisible();
        }

        // destroymap is called when we're done with a map and it can be destroyed
        public override void DestroyMap(MapId map)
        {
            for (int i = drops.Count - 1; i >= 0; i = Mathf.Min(i, drops.Count) - 1)
            {
                if (drops[i].OnMap(map))
                {
                    drops[i].Destroy();
                }
            }

            base.DestroyMap(map);
        }

        // resetgame is called whenever we go from a saved game to another or to a new game
        public override void ResetGame()
        {
            base.ResetGame();

            for (int i = drops.Count - 1; i >= 0; --i)
            {
                drops[i].Destroy();
            }
            OctoberUtils.Assert(drops.Count == 0);
            OctoberUtils.Assert(dropBehs.Count == 0);
        }

        // Called when simulation portion of new game is complete
        // setup next drop!
        public override void StartPlaying()
        {
            base.StartPlaying();

            nextDrop = TimeManager.Instance.seconds;
        }

        // Called when the visible slice levels have changed (alt + m mouse)
        public override void LevelChanged()
        {
            base.LevelChanged();

            RefreshVisible();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);

            // don't run when simulating
            if (ManagerBehavior.Instance.state != ManagerBehavior.GameState.Playing)
            {
                return;
            }

            // don't run when we aren't on a map
            if (MapManager.Instance.map == MapId.None)
            {
                return;
            }

            // don't run when there isn't a local map
            if (!TerrainManager.Instance.HasMap())
            {
                return;
            }

            // time to drop?
            if (TimeManager.Instance.seconds >= nextDrop)
            {
                // space for a new drop?
                if (drops.Count < dropLimit)
                {
                    // generate a random tile position
                    TilePos tile = (new TilePos(UnityEngine.Random.Range(-TerrainManager.Instance.mapRadius + 1, TerrainManager.Instance.mapRadius), 0, UnityEngine.Random.Range(-TerrainManager.Instance.mapRadius + 1, TerrainManager.Instance.mapRadius))).Clamped();

                    // sample the topmost world position
                    tile.y = TerrainManager.Instance.GetHeight(tile.x, tile.z);

                    // find the closest nav position
                    if (PathingManager.Instance.CloseNavSpot(null, tile, ref tile, BlockingQuery.Pathing, CloseNavSpotFlags.ReachableOnSurface))
                    {
                        // don't spawn in range of the base
                        if (!ZoneManager.Instance.IsHome(tile))
                        {
                            // pick a type
                            DropType type = null;
                            float totalWeight = 0f;
                            foreach (var potType in types)
                            {
                                if (potType.weight > 0f)
                                {
                                    totalWeight += potType.weight;
                                    if (Random.value * totalWeight <= potType.weight)
                                    {
                                        type = potType;
                                    }
                                }
                            }

                            if (type != null)
                            {
                                // spawn the drop (this will call add on us above and refresh its behavior)
                                new DropActor(type, tile);

                                // found one!
                                nextDrop = TimeManager.Instance.seconds + TimeManager.SecondsPerDay / (attemptsPerDay * OctoberMath.DistributedRandom(.5f, 2f));
                            }
                        }
                    }
                }
                else
                {
                    // try again later
                    nextDrop = TimeManager.Instance.seconds + TimeManager.SecondsPerDay / (attemptsPerDay * OctoberMath.DistributedRandom(.5f, 2f));
                }
            }

            // tick drops, in reverse order in case one times out and destroys (and removes itself)
            for (int i = drops.Count - 1; i >= 0; --i)
            {
                DropActor drop = drops[i];
                drop.Tick((float)(TimeManager.Instance.seconds - drop.lastTick));
                drop.SetLastTick(TimeManager.Instance.seconds);
            }
        }

        // refresh all behaviors
        public void RefreshVisible()
        {
            foreach (DropActor drop in drops)
            {
                RefreshBehavior(drop);
            }
        }

        // update the behavior (unity representaiton) of a drop
        public void RefreshBehavior(DropActor drop)
        {
            if (!drop.onCurrentMap)
            {
                if (drop.dropBeh != null)
                {
                    GameObject.Destroy(drop.dropBeh);
                    drop.SetBehavior(null);
                }
                return;
            }

            DropBehavior dropBeh = drop.dropBeh;
            if (dropBeh == null)
            {
                if (drop.type.prefab != null)
                {
                    // instance the behavior
                    dropBeh = OctBehavior.InstantiatePrefab(drop.type.prefab) as DropBehavior;
                    dropBeh.transform.position = drop.pos;
                    dropBeh.transform.localRotation = drop.rot;
                    dropBeh.transform.parent = ManagerBehavior.Instance.transform;
                    drop.SetBehavior(dropBeh);
                }
            }

            if (dropBeh != null)
            {
                // above or below the cut line?
                if (PropManager.Instance.LayerHidden(drop.tilePos))
                {
                    if (dropBeh.visible)
                    {
                        dropBeh.Hide();
                    }
                }
                else if (!dropBeh.visible)
                {
                    dropBeh.Show();
                }
            }
        }
    }
}
