// THE LOST TEMPLE - complete game in one script.
// Drop into Assets/Scripts in any 2D Unity project. Press Play. No scene setup needed.
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

static class In
{
    public static Vector2 Move()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; if (k == null) return Vector2.zero;
        float x = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1 : 0) - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1 : 0);
        float y = ((k.wKey.isPressed || k.upArrowKey.isPressed) ? 1 : 0) - ((k.sKey.isPressed || k.downArrowKey.isPressed) ? 1 : 0);
#else
        float x = Input.GetAxisRaw("Horizontal"), y = Input.GetAxisRaw("Vertical");
#endif
        var v = new Vector2(x, y); return v.sqrMagnitude > 1 ? v.normalized : v;
    }
    public static bool Attack()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }
    public static bool Pause()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
#endif
    }
    public static bool Confirm()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
    }
}

public class TempleGame : MonoBehaviour
{
    // ---------- TWEAKABLE SETTINGS (change these live during evaluation) ----------
    const float PlayerSpeed = 3.6f;
    const int StartLives = 3;
    const int TotalLevels = 3;
    const float SightRadius = 6.5f;      // torch light radius
    const int ScoreKey = 100, ScoreTreasure = 100, ScoreSpider = 50, ScoreHeart = 500;
    static readonly int[] LW = { 21, 25, 31 };   // maze width per level (odd numbers)
    static readonly int[] LH = { 15, 17, 21 };   // maze height per level (odd numbers)

    static TempleGame inst;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (inst == null) new GameObject("TempleGame").AddComponent<TempleGame>(); }

    enum St { Menu, Play, Paused, LevelDone, Over, Win }
    St state = St.Menu;

    class Thing { public GameObject g; public Vector2Int tile; public SpriteRenderer[] rs; public Color[] cs; public float min; }
    class Spider { public Thing th; public Vector2 pos; public Vector2Int tgt, prev; public float spd; }
    class Trap { public Thing th; public float phase; }

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    // world
    int W, H; bool[,] wall; bool[,] seen; float[,] light; SpriteRenderer[,] tiles; Color[,] baseCol;
    Transform world; Camera cam; AudioSource sfx;
    Sprite sCircle, sSquare, sTri;
    List<Thing> keys = new List<Thing>(), gems = new List<Thing>();
    List<Spider> spiders = new List<Spider>(); List<Trap> traps = new List<Trap>();
    Thing gate, heartTh, exitTh; Vector2Int gateTile, heartTile, exitTile;
    bool gateOpen, hasHeart, exitOpen, sealing;
    float sealTimer, sealTime;

    // player
    GameObject playerRoot, swingFx; Transform torch; SpriteRenderer[] pRs; Color[] pCs;
    Vector2 pPos, face = Vector2.right; float invuln, cd, swingT;
    int lvl = 1, score, lives, keysHeld, keysNeed, lastBonus; string pName = "Explorer";
    string msg = ""; float msgT;

    // gui
    GUIStyle sTitle, sMid, sTxt, sBtn, sLeft, sRight, sField; Texture2D white;

    // ---------------------------------------------------------------- setup
    void Awake()
    {
        inst = this;
        sCircle = MakeSprite(64, (u, v) => u * u + v * v <= 1f);
        sSquare = MakeSprite(4, (u, v) => true);
        sTri = MakeSprite(64, (u, v) => Mathf.Abs(u) <= (1f - v) * 0.5f);
        white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();

        cam = Camera.main;
        if (cam == null)
        {
            var cg = new GameObject("Main Camera"); cg.tag = "MainCamera";
            cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>();
        }
        cam.orthographic = true; cam.orthographicSize = 5.5f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.04f, 0.11f);
        cam.transform.position = new Vector3(0, 0, -10); cam.transform.rotation = Quaternion.identity;

        sfx = gameObject.AddComponent<AudioSource>();
        pName = PlayerPrefs.GetString("player_name", "Explorer");
        BuildPlayer();
        playerRoot.SetActive(false);
    }

    static Sprite MakeSprite(int n, System.Func<float, float, bool> f)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false); t.filterMode = FilterMode.Bilinear;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            { float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1; px[y * n + x] = f(u, v) ? Color.white : new Color(1, 1, 1, 0); }
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
    }

    GameObject Spawn(string n, Sprite sp, Color c, Vector2 pos, Vector2 sc, int order, Transform parent = null, float rot = 0)
    {
        var g = new GameObject(n); g.transform.SetParent(parent != null ? parent : world, false);
        g.transform.localPosition = pos; g.transform.localScale = new Vector3(sc.x, sc.y, 1);
        g.transform.localRotation = Quaternion.Euler(0, 0, rot);
        var r = g.AddComponent<SpriteRenderer>(); r.sprite = sp; r.color = c; r.sortingOrder = order; return g;
    }
    GameObject Root(string n, Vector2 pos) { var g = new GameObject(n); g.transform.SetParent(world, false); g.transform.localPosition = pos; return g; }
    Thing MakeThing(GameObject g, Vector2Int tile, float min = 0)
    {
        var t = new Thing { g = g, tile = tile, min = min };
        t.rs = g.GetComponentsInChildren<SpriteRenderer>(); t.cs = new Color[t.rs.Length];
        for (int i = 0; i < t.rs.Length; i++) t.cs[i] = t.rs[i].color; return t;
    }
    void Tint(Thing t, float b)
    {
        b = Mathf.Max(b, t.min);
        for (int i = 0; i < t.rs.Length; i++) { var c = t.cs[i]; t.rs[i].color = new Color(c.r * b, c.g * b, c.b * b, c.a); }
    }

    void BuildPlayer()
    {
        playerRoot = new GameObject("Player"); playerRoot.transform.SetParent(transform, false);
        var p = playerRoot.transform;
        Spawn("shadow", sCircle, new Color(0, 0, 0, .35f), new Vector2(0.04f, -0.04f), Vector2.one * .7f, 9, p);
        Spawn("pack", sSquare, new Color(.4f, .25f, .1f), new Vector2(0, -.2f), new Vector2(.42f, .3f), 10, p);
        Spawn("body", sCircle, new Color(.82f, .66f, .42f), Vector2.zero, Vector2.one * .55f, 11, p);
        Spawn("brim", sCircle, new Color(.5f, .32f, .14f), new Vector2(0, .02f), Vector2.one * .5f, 12, p);
        Spawn("hat", sCircle, new Color(.38f, .23f, .08f), new Vector2(0, .02f), Vector2.one * .26f, 13, p);
        var t = Spawn("torch", sCircle, new Color(1f, .6f, .1f), Vector2.zero, Vector2.one * .22f, 14, p);
        torch = t.transform;
        Spawn("glow", sCircle, new Color(1f, .75f, .25f, .3f), Vector2.zero, Vector2.one * 3.2f, 13, torch);
        pRs = playerRoot.GetComponentsInChildren<SpriteRenderer>(); pCs = new Color[pRs.Length];
        for (int i = 0; i < pRs.Length; i++) pCs[i] = pRs[i].color;
        swingFx = Spawn("swing", sCircle, new Color(1f, .7f, .2f, .55f), Vector2.zero, Vector2.one * 1.6f, 15, p);
        swingFx.transform.SetParent(null, true); swingFx.transform.SetParent(transform, true); swingFx.SetActive(false);
    }

    // ---------------------------------------------------------------- level generation
    int[,] Bfs(Vector2Int s)
    {
        var d = new int[W, H]; for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) d[x, y] = -1;
        var q = new Queue<Vector2Int>(); d[s.x, s.y] = 0; q.Enqueue(s);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var dir in Dirs)
            {
                var n = c + dir;
                if (n.x < 0 || n.y < 0 || n.x >= W || n.y >= H || wall[n.x, n.y] || d[n.x, n.y] >= 0) continue;
                d[n.x, n.y] = d[c.x, c.y] + 1; q.Enqueue(n);
            }
        }
        return d;
    }
    int OpenN(int x, int y)
    {
        int c = 0; foreach (var d in Dirs) { int nx = x + d.x, ny = y + d.y; if (nx >= 0 && ny >= 0 && nx < W && ny < H && !wall[nx, ny]) c++; }
        return c;
    }

    void GenMaze()
    {
        wall = new bool[W, H]; for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) wall[x, y] = true;
        var st = new Stack<Vector2Int>(); wall[1, 1] = false; st.Push(new Vector2Int(1, 1));
        while (st.Count > 0)
        {
            var c = st.Peek(); var opts = new List<Vector2Int>();
            foreach (var d in Dirs) { var n = c + d * 2; if (n.x > 0 && n.y > 0 && n.x < W - 1 && n.y < H - 1 && wall[n.x, n.y]) opts.Add(d); }
            if (opts.Count == 0) { st.Pop(); continue; }
            var dd = opts[Random.Range(0, opts.Count)];
            wall[c.x + dd.x, c.y + dd.y] = false; var nn = c + dd * 2; wall[nn.x, nn.y] = false; st.Push(nn);
        }
    }

    void BuildLevel()
    {
        if (world != null) Destroy(world.gameObject);
        world = new GameObject("World").transform;
        keys.Clear(); gems.Clear(); spiders.Clear(); traps.Clear();
        gateOpen = hasHeart = exitOpen = sealing = false; keysHeld = 0; invuln = 0; cd = 0;
        W = LW[lvl - 1]; H = LH[lvl - 1];
        GenMaze();
        var start = new Vector2Int(1, 1);
        var dist = Bfs(start);

        // Heart of Eternity = farthest dead end. Gate = its only neighbour.
        heartTile = start; int best = -1;
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
            if (!wall[x, y] && dist[x, y] > best && OpenN(x, y) == 1 && !(x == 1 && y == 1)) { best = dist[x, y]; heartTile = new Vector2Int(x, y); }
        gateTile = heartTile; foreach (var d in Dirs) { var n = heartTile + d; if (!wall[n.x, n.y]) gateTile = n; }

        // extra loops so the maze is not a single path
        int made = 0, tries = W * H;
        for (int i = 0; i < tries && made < W * H / 12; i++)
        {
            int x = Random.Range(1, W - 1), y = Random.Range(1, H - 1);
            if (!wall[x, y]) continue;
            Vector2Int a, b;
            if (x % 2 == 0 && y % 2 == 1) { a = new Vector2Int(x - 1, y); b = new Vector2Int(x + 1, y); }
            else if (x % 2 == 1 && y % 2 == 0) { a = new Vector2Int(x, y - 1); b = new Vector2Int(x, y + 1); }
            else continue;
            if (wall[a.x, a.y] || wall[b.x, b.y]) continue;
            if (Mathf.Abs(x - heartTile.x) + Mathf.Abs(y - heartTile.y) <= 1) continue;
            wall[x, y] = false; made++;
        }
        exitTile = new Vector2Int(0, 1); wall[0, 1] = false;
        dist = Bfs(start);

        // tiles
        tiles = new SpriteRenderer[W, H]; baseCol = new Color[W, H]; seen = new bool[W, H]; light = new float[W, H];
        var tr = new GameObject("Tiles").transform; tr.SetParent(world, false);
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
        {
            Color c; float v = Random.Range(-.04f, .04f);
            if (wall[x, y]) { c = Random.value < .15f ? new Color(.24f + v, .36f + v, .2f + v) : new Color(.34f + v, .27f + v, .22f + v); }
            else c = new Color(.62f + v, .53f + v, .42f + v);
            baseCol[x, y] = c;
            var g = Spawn("t", sSquare, c, new Vector2(x, y), Vector2.one, wall[x, y] ? 1 : 0, tr);
            tiles[x, y] = g.GetComponent<SpriteRenderer>();
        }

        // free tiles for placement
        var free = new List<Vector2Int>();
        for (int x = 1; x < W; x++) for (int y = 1; y < H; y++)
            if (!wall[x, y] && dist[x, y] >= 5 && new Vector2Int(x, y) != heartTile && new Vector2Int(x, y) != gateTile) free.Add(new Vector2Int(x, y));
        for (int i = free.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = free[i]; free[i] = free[j]; free[j] = t; }
        System.Func<Vector2Int> take = () => { if (free.Count == 0) return new Vector2Int(1, 1); var v = free[free.Count - 1]; free.RemoveAt(free.Count - 1); return v; };

        // gate
        var gg = Root("Gate", gateTile);
        Spawn("a", sSquare, new Color(.78f, .6f, .12f), Vector2.zero, Vector2.one * .94f, 5, gg.transform);
        Spawn("b", sSquare, new Color(.25f, .15f, .05f), Vector2.zero, new Vector2(.12f, .8f), 6, gg.transform);
        Spawn("c", sCircle, new Color(.2f, .1f, .03f), Vector2.zero, Vector2.one * .3f, 7, gg.transform);
        gate = MakeThing(gg, gateTile, .55f);

        // heart
        var hg = Root("Heart", heartTile);
        Spawn("halo", sCircle, new Color(1f, .85f, .2f, .35f), Vector2.zero, Vector2.one * 1.3f, 5, hg.transform);
        Spawn("d", sSquare, new Color(1f, .8f, .1f), Vector2.zero, Vector2.one * .5f, 6, hg.transform, 45);
        Spawn("e", sSquare, new Color(1f, .95f, .55f), Vector2.zero, Vector2.one * .24f, 7, hg.transform, 45);
        heartTh = MakeThing(hg, heartTile, .8f);

        // exit door (on outer wall next to start)
        var eg = Root("Exit", exitTile);
        Spawn("a", sSquare, new Color(.45f, .14f, .1f), Vector2.zero, Vector2.one, 5, eg.transform);
        Spawn("b", sSquare, new Color(.2f, .06f, .04f), Vector2.zero, new Vector2(.14f, .9f), 6, eg.transform);
        exitTh = MakeThing(eg, exitTile, .6f);

        keysNeed = lvl + 1;
        for (int i = 0; i < keysNeed; i++)
        {
            var t = take(); var g = Root("Key", t);
            Spawn("r", sCircle, new Color(1f, .82f, .15f), new Vector2(0, .14f), Vector2.one * .3f, 6, g.transform);
            Spawn("h", sCircle, new Color(.3f, .2f, .05f), new Vector2(0, .14f), Vector2.one * .12f, 7, g.transform);
            Spawn("s", sSquare, new Color(1f, .82f, .15f), new Vector2(0, -.1f), new Vector2(.1f, .42f), 6, g.transform);
            Spawn("t", sSquare, new Color(1f, .82f, .15f), new Vector2(.08f, -.2f), new Vector2(.18f, .08f), 6, g.transform);
            keys.Add(MakeThing(g, t));
        }
        for (int i = 0; i < 4 + lvl; i++)
        {
            var t = take(); var g = Root("Treasure", t);
            Spawn("c", sSquare, new Color(.55f, .33f, .12f), Vector2.zero, new Vector2(.52f, .36f), 6, g.transform);
            Spawn("b", sSquare, new Color(.95f, .75f, .2f), Vector2.zero, new Vector2(.52f, .1f), 7, g.transform);
            gems.Add(MakeThing(g, t));
        }
        for (int i = 0; i < 3 + lvl * 3; i++)
        {
            var t = take(); var g = Root("Trap", t);
            Spawn("plate", sSquare, new Color(.3f, .3f, .33f), Vector2.zero, Vector2.one * .8f, 4, g.transform);
            for (int k = -1; k <= 1; k++) Spawn("spike", sTri, new Color(.88f, .88f, .93f), new Vector2(k * .24f, 0), new Vector2(.2f, .5f), 5, g.transform);
            traps.Add(new Trap { th = MakeThing(g, t), phase = Random.Range(0f, 3f) });
        }
        for (int i = 0; i < lvl * 2; i++)
        {
            var t = take(); var g = Root("Spider", t);
            var dark = new Color(.2f, .1f, .3f);
            foreach (float ang in new[] { 20f, 70f, 110f, 160f }) Spawn("leg", sSquare, dark, Vector2.zero, new Vector2(1.05f, .07f), 7, g.transform, ang);
            Spawn("body", sCircle, new Color(.3f, .13f, .42f), Vector2.zero, Vector2.one * .5f, 8, g.transform);
            Spawn("head", sCircle, new Color(.25f, .1f, .36f), new Vector2(0, .27f), Vector2.one * .28f, 8, g.transform);
            Spawn("e1", sCircle, new Color(1f, .1f, .1f), new Vector2(-.07f, .33f), Vector2.one * .1f, 9, g.transform);
            Spawn("e2", sCircle, new Color(1f, .1f, .1f), new Vector2(.07f, .33f), Vector2.one * .1f, 9, g.transform);
            spiders.Add(new Spider { th = MakeThing(g, t), pos = t, tgt = t, prev = t, spd = 1.4f + .25f * lvl });
        }

        sealTime = dist[heartTile.x, heartTile.y] / PlayerSpeed * (1.9f - .2f * lvl) + 8f;
        pPos = new Vector2(1, 1); playerRoot.SetActive(true);
        cam.transform.position = new Vector3(pPos.x, pPos.y, -10);
        Say(lvl == 1 ? "The door slammed shut! Find " + keysNeed + " keys to open the golden gate." : "Level " + lvl + ": find " + keysNeed + " keys!", 5f);
    }

    // ---------------------------------------------------------------- game flow
    void StartGame()
    {
        pName = pName.Trim(); if (pName.Length == 0) pName = "Explorer";
        PlayerPrefs.SetString("player_name", pName);
        PlayerPrefs.SetInt("games_" + pName, PlayerPrefs.GetInt("games_" + pName, 0) + 1);
        score = 0; lives = StartLives; lvl = 1; BuildLevel(); state = St.Play;
    }
    void NextLevel() { lvl++; BuildLevel(); state = St.Play; }
    void Say(string s, float t = 3.5f) { msg = s; msgT = t; }
    void Beep(float f, float d, bool slide = false)
    {
        int n = (int)(44100 * d); var data = new float[n];
        for (int i = 0; i < n; i++) { float t = i / 44100f; float fr = slide ? f * (1f - t / d * .6f) : f; data[i] = Mathf.Sin(2 * Mathf.PI * fr * t) * .25f * (1f - (float)i / n); }
        var c = AudioClip.Create("b", n, 1, 44100, false); c.SetData(data, 0); sfx.PlayOneShot(c);
    }
    void SaveScore()
    {
        string k = "best_" + pName;
        if (score > PlayerPrefs.GetInt(k, 0)) PlayerPrefs.SetInt(k, score);
        if (score > PlayerPrefs.GetInt("hs", 0)) { PlayerPrefs.SetInt("hs", score); PlayerPrefs.SetString("hs_name", pName); }
        PlayerPrefs.Save();
    }
    void EndGame(bool win) { SaveScore(); playerRoot.SetActive(false); state = win ? St.Win : St.Over; }
    void Hurt(string why)
    {
        if (invuln > 0) return;
        lives--; invuln = 1.6f; Beep(150, .3f, true); Say(why, 2f);
        if (lives <= 0) EndGame(false);
    }
    void LoseLevel(string why)
    {
        lives--; Beep(120, .5f, true);
        if (lives <= 0) { EndGame(false); return; }
        BuildLevel(); Say(why + " Try again!", 4f);
    }

    // ---------------------------------------------------------------- per-frame
    void Update()
    {
        if (msgT > 0) msgT -= Time.deltaTime;
        if (state == St.Play) Tick();
        else if (state == St.Paused) { if (In.Pause()) state = St.Play; }
        else if (In.Confirm())
        {
            if (state == St.Menu) StartGame();
            else if (state == St.LevelDone) NextLevel();
            else state = St.Menu;
        }
    }

    bool WalkT(int x, int y, bool spider)
    {
        if (x < 0 || y < 0 || x >= W || y >= H || wall[x, y]) return false;
        if (!gateOpen && x == gateTile.x && y == gateTile.y) return false;
        if (x == exitTile.x && y == exitTile.y && (spider || !exitOpen)) return false;
        return true;
    }
    bool Free(Vector2 p, float r)
    {
        return WalkT(Mathf.RoundToInt(p.x - r), Mathf.RoundToInt(p.y - r), false) && WalkT(Mathf.RoundToInt(p.x + r), Mathf.RoundToInt(p.y - r), false)
            && WalkT(Mathf.RoundToInt(p.x - r), Mathf.RoundToInt(p.y + r), false) && WalkT(Mathf.RoundToInt(p.x + r), Mathf.RoundToInt(p.y + r), false);
    }
    void MovePlayer(Vector2 m, float dt)
    {
        float s = PlayerSpeed * dt;
        if (Mathf.Abs(m.x) > .01f)
        {
            var np = pPos + new Vector2(m.x * s, 0);
            if (Free(np, .3f)) pPos = np;
            else if (Mathf.Abs(m.y) < .01f) pPos.y = Mathf.MoveTowards(pPos.y, Mathf.Round(pPos.y), s);
        }
        if (Mathf.Abs(m.y) > .01f)
        {
            var np = pPos + new Vector2(0, m.y * s);
            if (Free(np, .3f)) pPos = np;
            else if (Mathf.Abs(m.x) < .01f) pPos.x = Mathf.MoveTowards(pPos.x, Mathf.Round(pPos.x), s);
        }
    }

    void Tick()
    {
        float dt = Time.deltaTime;
        if (In.Pause()) { state = St.Paused; return; }
        var m = In.Move();
        if (m.sqrMagnitude > .01f) { face = m.normalized; MovePlayer(m, dt); }
        invuln = Mathf.Max(0, invuln - dt); cd -= dt;
        if (In.Attack() && cd <= 0) Swing();
        if (swingT > 0) { swingT -= dt; if (swingT <= 0) swingFx.SetActive(false); }

        UpdateTraps(); UpdateSpiders(dt); if (state != St.Play) return;
        Pickups(); if (state != St.Play) return;

        if (sealing)
        {
            sealTimer -= dt;
            if (sealTimer <= 0) { LoseLevel("The temple sealed shut!"); return; }
        }
        var pt = new Vector2Int(Mathf.RoundToInt(pPos.x), Mathf.RoundToInt(pPos.y));
        if (exitOpen && pt == exitTile)
        {
            lastBonus = Mathf.CeilToInt(sealTimer) * 10 + lives * 100; score += lastBonus; Beep(880, .5f);
            if (lvl >= TotalLevels) EndGame(true); else { SaveScore(); state = St.LevelDone; }
        }
    }

    void Swing()
    {
        cd = .45f; swingT = .12f; Beep(420, .12f, true);
        swingFx.transform.position = pPos + face * .7f; swingFx.SetActive(true);
        for (int i = spiders.Count - 1; i >= 0; i--)
            if (Vector2.Distance(spiders[i].pos, pPos) < 1.5f)
            { Destroy(spiders[i].th.g); spiders.RemoveAt(i); score += ScoreSpider; Beep(700, .15f); }
    }

    void UpdateTraps()
    {
        foreach (var t in traps)
        {
            float c = (Time.time + t.phase) % 3f; bool warn = c > 1.7f && c < 2.1f, on = c >= 2.1f;
            t.th.cs[0] = warn ? new Color(.75f, .2f, .12f) : new Color(.3f, .3f, .33f);
            for (int i = 1; i < t.th.rs.Length; i++) t.th.rs[i].enabled = on;
            if (on && Vector2.Distance(pPos, t.th.tile) < .45f) Hurt("Spikes!");
        }
    }
    void UpdateSpiders(float dt)
    {
        foreach (var s in spiders)
        {
            var tp = (Vector2)s.tgt; s.pos = Vector2.MoveTowards(s.pos, tp, s.spd * dt);
            if ((s.pos - tp).sqrMagnitude < .0001f)
            {
                var cur = s.tgt; var opts = new List<Vector2Int>();
                foreach (var d in Dirs) { var n = cur + d; if (WalkT(n.x, n.y, true) && n != s.prev) opts.Add(n); }
                if (opts.Count == 0) opts.Add(s.prev);
                Vector2Int pick;
                if ((s.pos - pPos).magnitude < 7f)
                {
                    pick = opts[0]; float bd = 1e9f;
                    foreach (var o in opts) { float dd = ((Vector2)o - pPos).sqrMagnitude; if (dd < bd) { bd = dd; pick = o; } }
                }
                else pick = opts[Random.Range(0, opts.Count)];
                s.prev = cur; s.tgt = pick;
            }
            var dir = tp - s.pos;
            if (dir.sqrMagnitude > .001f) s.th.g.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            s.th.g.transform.position = s.pos;
            if (Vector2.Distance(s.pos, pPos) < .55f) Hurt("A spider bit you!");
        }
    }

    void Pickups()
    {
        for (int i = keys.Count - 1; i >= 0; i--)
            if (Vector2.Distance(pPos, keys[i].tile) < .5f)
            {
                Destroy(keys[i].g); keys.RemoveAt(i); keysHeld++; score += ScoreKey; Beep(1200, .2f);
                if (keysHeld >= keysNeed)
                {
                    gateOpen = true; Destroy(gate.g); Beep(500, .5f);
                    Say("The golden gate is unlocked! Find the Heart of Eternity!", 5f);
                }
                else Say("Key found! " + (keysNeed - keysHeld) + " to go.", 2f);
            }
        for (int i = gems.Count - 1; i >= 0; i--)
            if (Vector2.Distance(pPos, gems[i].tile) < .5f)
            { Destroy(gems[i].g); gems.RemoveAt(i); score += ScoreTreasure; Beep(950, .15f); Say("Treasure! +" + ScoreTreasure, 1.5f); }
        if (gateOpen && !hasHeart && Vector2.Distance(pPos, heartTile) < .55f)
        {
            hasHeart = true; Destroy(heartTh.g); score += ScoreHeart; Beep(660, .6f);
            sealing = true; sealTimer = sealTime; exitOpen = true;
            exitTh.cs[0] = new Color(.3f, 1f, .4f); exitTh.cs[1] = new Color(.9f, 1f, .8f);
            Say("HEART OF ETERNITY! The temple is sealing - RUN to the green door!", 5f);
        }
    }

    void LateUpdate()
    {
        if (world == null || state == St.Menu) return;
        float R = SightRadius, flick = .93f + .07f * Mathf.Sin(Time.time * 13f) * Mathf.Sin(Time.time * 7.3f);
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
        {
            float d = Mathf.Sqrt((x - pPos.x) * (x - pPos.x) + (y - pPos.y) * (y - pPos.y));
            float b = Mathf.Clamp01(1f - d / R) * flick;
            if (d < R) seen[x, y] = true;
            float f = seen[x, y] ? Mathf.Max(b, .22f) : b + .02f;
            light[x, y] = f; var c = baseCol[x, y]; tiles[x, y].color = new Color(c.r * f, c.g * f, c.b * f, 1);
        }
        foreach (var k in keys) VisStatic(k);
        foreach (var k in gems) VisStatic(k);
        foreach (var t in traps) VisStatic(t.th);
        if (gate != null && gate.g != null) VisStatic(gate);
        if (heartTh != null && heartTh.g != null) { VisStatic(heartTh); heartTh.g.transform.localScale = Vector3.one * (1f + .12f * Mathf.Sin(Time.time * 4f)); }
        VisStatic(exitTh);
        foreach (var s in spiders)
        {
            float d = Vector2.Distance(s.pos, pPos); bool on = d < R + .5f; s.th.g.SetActive(on);
            if (on) Tint(s.th, Mathf.Clamp01(1f - d / R) * flick + .1f);
        }
        // player visuals
        playerRoot.transform.position = pPos;
        torch.localPosition = face * .38f; torch.localScale = Vector3.one * (.22f + .03f * Mathf.Sin(Time.time * 20f));
        bool blink = invuln > 0 && Mathf.Sin(Time.time * 40f) > 0;
        for (int i = 0; i < pRs.Length; i++) { var c = pCs[i]; pRs[i].color = new Color(c.r, c.g, c.b, blink ? c.a * .3f : c.a); }
        // camera
        var target = new Vector3(pPos.x, pPos.y, -10);
        var cp = Vector3.Lerp(cam.transform.position, target, 10f * Time.deltaTime);
        if (sealing && state == St.Play) cp += (Vector3)(Random.insideUnitCircle * .05f);
        cam.transform.position = cp;
    }
    void VisStatic(Thing t)
    {
        bool on = seen[t.tile.x, t.tile.y]; t.g.SetActive(on);
        if (on) Tint(t, light[t.tile.x, t.tile.y]);
    }

    // ---------------------------------------------------------------- GUI
    void Lbl(Rect r, string s, GUIStyle st, Color c)
    {
        var o = st.normal.textColor; st.normal.textColor = Color.black; GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        st.normal.textColor = c; GUI.Label(r, s, st); st.normal.textColor = o;
    }
    void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }

    void OnGUI()
    {
        if (sTxt == null)
        {
            sTxt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sTitle = new GUIStyle(sTxt) { fontStyle = FontStyle.Bold };
            sMid = new GUIStyle(sTxt) { fontStyle = FontStyle.Bold };
            sLeft = new GUIStyle(sTxt) { alignment = TextAnchor.MiddleLeft };
            sRight = new GUIStyle(sTxt) { alignment = TextAnchor.MiddleRight };
            sBtn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            sField = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter };
        }
        float w = Screen.width, h = Screen.height; int u = Mathf.Max(14, Screen.height / 32);
        sTxt.fontSize = sLeft.fontSize = sRight.fontSize = u; sMid.fontSize = (int)(u * 1.5f); sTitle.fontSize = u * 3;
        sBtn.fontSize = (int)(u * 1.2f); sField.fontSize = (int)(u * 1.2f);
        var gold = new Color(1f, .82f, .25f);

        if (state == St.Menu)
        {
            Fill(new Rect(0, 0, w, h), new Color(.1f, .06f, .16f, .96f));
            Lbl(new Rect(0, h * .04f, w, h * .15f), "THE LOST TEMPLE", sTitle, gold);
            Lbl(new Rect(0, h * .19f, w, h * .06f), "Explore  -  Solve  -  Escape", sMid, Color.white);
            Lbl(new Rect(0, h * .29f, w, h * .05f), "Explorer name:", sTxt, Color.white);
            pName = GUI.TextField(new Rect(w * .35f, h * .34f, w * .3f, h * .07f), pName, 14, sField);
            Lbl(new Rect(w * .08f, h * .43f, w * .84f, h * .32f),
                "WASD / Arrows: move     SPACE: swing torch (kills spiders)     ESC: pause\n\n" +
                "1. Collect all the KEYS to open the golden gate.\n2. Grab the HEART OF ETERNITY.\n" +
                "3. RUN back to the exit door before the temple seals!\nAvoid SPIKES and SPIDERS. You have " + StartLives + " lives.",
                sTxt, Color.white);
            string n = pName.Trim().Length == 0 ? "Explorer" : pName.Trim();
            Lbl(new Rect(0, h * .75f, w, h * .06f), "Best score (" + n + "): " + PlayerPrefs.GetInt("best_" + n, 0) + "   |   Games: " + PlayerPrefs.GetInt("games_" + n, 0) +
                "   |   Hall of fame: " + PlayerPrefs.GetString("hs_name", "-") + " " + PlayerPrefs.GetInt("hs", 0), sTxt, gold);
            if (GUI.Button(new Rect(w * .35f, h * .83f, w * .3f, h * .09f), "ENTER THE TEMPLE", sBtn)) StartGame();
            if (!Application.isEditor && GUI.Button(new Rect(w * .88f, h * .93f, w * .1f, h * .05f), "Quit", sBtn)) Application.Quit();
            return;
        }

        // HUD
        Fill(new Rect(0, 0, w, h * .075f), new Color(0, 0, 0, .6f));
        Lbl(new Rect(w * .01f, 0, w * .35f, h * .075f), pName + "   Level " + lvl + "/" + TotalLevels, sLeft, Color.white);
        Lbl(new Rect(w * .36f, 0, w * .28f, h * .075f), "Score " + score, sTxt, gold);
        Lbl(new Rect(w * .6f, 0, w * .39f, h * .075f), "Lives " + lives + "   Keys " + keysHeld + "/" + keysNeed, sRight, Color.white);
        string obj = hasHeart ? "ESCAPE through the green door!" : gateOpen ? "Find the Heart of Eternity!" : "Find the keys (" + keysHeld + "/" + keysNeed + ") to open the golden gate";
        Lbl(new Rect(0, h * .075f, w, h * .05f), obj, sTxt, Color.white);
        if (sealing) Lbl(new Rect(0, h * .12f, w, h * .08f), "TEMPLE SEALS IN " + Mathf.CeilToInt(sealTimer) + "s", sMid, new Color(1f, .3f, .25f));
        if (msgT > 0) Lbl(new Rect(w * .1f, h * .85f, w * .8f, h * .1f), msg, sTxt, Color.white);
        if (invuln > 1.1f) Fill(new Rect(0, 0, w, h), new Color(1, 0, 0, (invuln - 1.1f) * .6f));

        if (state == St.Play) return;
        Fill(new Rect(0, 0, w, h), new Color(0, 0, 0, .75f));
        if (state == St.Paused)
        {
            Lbl(new Rect(0, h * .25f, w, h * .12f), "PAUSED", sTitle, gold);
            if (GUI.Button(new Rect(w * .35f, h * .45f, w * .3f, h * .08f), "Resume", sBtn)) state = St.Play;
            if (GUI.Button(new Rect(w * .35f, h * .56f, w * .3f, h * .08f), "Main menu", sBtn)) { playerRoot.SetActive(false); state = St.Menu; }
        }
        else if (state == St.LevelDone)
        {
            Lbl(new Rect(0, h * .2f, w, h * .12f), "LEVEL " + lvl + " CLEARED!", sTitle, gold);
            Lbl(new Rect(0, h * .38f, w, h * .2f), "Time + lives bonus: " + lastBonus + "\nScore: " + score, sMid, Color.white);
            if (GUI.Button(new Rect(w * .35f, h * .65f, w * .3f, h * .09f), "Next level", sBtn)) NextLevel();
        }
        else
        {
            bool win = state == St.Win;
            Lbl(new Rect(0, h * .15f, w, h * .14f), win ? "YOU ESCAPED!" : "GAME OVER", sTitle, win ? gold : new Color(1f, .3f, .25f));
            Lbl(new Rect(0, h * .32f, w, h * .3f), (win ? "The Heart of Eternity is yours, " + pName + "!\n" : "The temple claims another explorer...\n") +
                "Final score: " + score + "\nYour best: " + PlayerPrefs.GetInt("best_" + pName, 0) + "   Hall of fame: " + PlayerPrefs.GetString("hs_name", "-") + " " + PlayerPrefs.GetInt("hs", 0), sMid, Color.white);
            if (GUI.Button(new Rect(w * .35f, h * .68f, w * .3f, h * .09f), "Main menu", sBtn)) state = St.Menu;
        }
    }
}
