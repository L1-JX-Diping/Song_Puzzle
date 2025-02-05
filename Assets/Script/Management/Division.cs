﻿using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text.RegularExpressions;
using Color = UnityEngine.Color;
using Random = UnityEngine.Random;
using UnityEngine.Rendering;
using System;
using System.Reflection;

public class Division 
{
    /* private な変数たち */
    // 色弱用 GREEN, *(=Heart) 色とマークの対応
    private Dictionary<Color, string> _avatarDict = new Dictionary<Color, string>();
    private string _lyricsFileName = ""; // 入力ファイル名（Assetsフォルダ内）
    // for loading from file
    private List<Player> _playerList = new List<Player>();
    // for creating and exporting to file
    private List<Line> _lyricsList = new List<Line>(); // lyrics information 
    // 
    private float _clock = 3f; // Second per Beat
    private float _beat = 4; // 何拍子か？ Birthday song は 3 拍子
    private string _eofText = "GAME END.";
    private List<Color> _colorList = new List<Color>();
    // do not move
    private int _indexForNumList = 0;

    // Start is called before the first frame update
    void Start()
    {
        // initiallize game data such as _playerRole
        InitGameData();
        
        SetColorList(); // color - avatar

        LoadLyricsFile(); // 歌詞ファイルを読み込む

        // Save information to files
        Common.ExportToXml(_lyricsList, FileName.XmlLyricsDivision);

        //ExportColorLog(); // 色分け情報を記録
        //ExportPartDivision(); // パート分け情報を記録
    }

    // Update is called once per frame
    void Update()
    {
    }

    private void InitGameData()
    {
        _playerList = (List<Player>)Common.LoadXml(_playerList.GetType(), FileName.XmlPlayerRole);
        SetLyricsFileName();
    }

    /// <summary>
    /// Set game data to use in this class
    /// </summary>
    private void SetLyricsFileName()
    {
        // set data to GameData class
        Data.SetAllDataFromTXT();

        // get from the data set to GameData class
        string songTitle = Data.SongTitle;
        _lyricsFileName = "Lyrics-" + songTitle + ".txt";
    }

    /// <summary>
    /// Get assigned colors and Set into _colorList
    /// </summary>
    private void SetColorList()
    {
        string[] lineList = Common.GetTXTFileLineList(FileName.PlayerRole);

        // format: PlayerName, ColorName, Avatar(Mark), Mic
        foreach (string line in lineList)
        {
            // separate by ","
            string[] playerRole = line.Split(',');

            /* set COLOR List */
            string colorName = playerRole[1].Trim(); // 2 列目: color name like "RED"
            Color color = Common.ToColor(colorName);
            _colorList.Add(color);

            /* set AVATAR dictionary */
            string avatar = playerRole[2].Trim(); // 3 行目: avatar like "Spade"
            // Add to Dictionary
            if (!_avatarDict.ContainsKey(color))
            {
                _avatarDict[color] = avatar;
            }
            else
            {
                Debug.LogWarning($"Duplicate color entry found: {colorName}, ignoring the second entry.");
            }
        }
        // for debug
        Debug.Log($"Set _markDict with {_avatarDict.Count} entries, _colorList with {_colorList.Count} entries.");
    }


    void LoadLyricsFile()
    {
        string[] lyricsLineList = Common.GetTXTFileLineList(_lyricsFileName);

        // Lyrics division
        CreateLyricsList(lyricsLineList);

        // Debug
        DebugToConsole();

    }

    private void DebugToConsole()
    {
        Debug.Log("_lyricsList: \n");
        foreach (Line line in _lyricsList)
        {
            Debug.Log(line.timing + ", " + line.text);
        }

        Debug.Log($"Loaded {_lyricsList.Count} lyrics from {_lyricsFileName}");
    }

    private void CreateLyricsList(string[] lyricsLineList)
    {
        float lineTiming = 0f;

        // 前奏 intro 部分用
        _lyricsList.Add(new Line { timing = 0.0f, text = "" });

        // meta info part (1行目) の処理
        // bpm と intro を取得
        if (lyricsLineList.Length > 0 && lyricsLineList[0].StartsWith("#"))
        {
            string metaLine = lyricsLineList[0];
            // 曲の speed 情報
            int bpm = ParseMetaLine(metaLine, "bpm");
            _beat = ParseMetaLine(metaLine, "beat");
            int introEndBeat = ParseMetaLine(metaLine, "intro");
            _clock = 60f / (float)bpm; // clock を計算
            // 歌詞スクロール計算の開始時刻
            lineTiming = introEndBeat * _clock; // lyricsStartTime
            Debug.Log($"Parsed BPM: {bpm} beats/min, beat: {_beat} count/bar, intro/startTime(init): {introEndBeat} beats, clock Interval: {_clock:F2} seconds");
        }
        else
        {
            Debug.LogError("Meta information not found in the first line.");
            return;
        }

        // lyrics part (2 行目以降) の処理
        // 歌詞の表示開始時間情報付き lyricsList を作成
        for (int i = 1; i < lyricsLineList.Length; i++)
        {
            string lyricsInfo = lyricsLineList[i];
            // Line ごとに更新
            List<int> ratioList = new List<int>();

            // 正規表現を使用してデータを抽出 
            // 例: 2[0,1,3,4]Happy birthday to you
            Regex regex = new Regex(@"(\d+)\[([0-9,]+)\](.*)");
            Match match = regex.Match(lyricsInfo);
            if (!match.Success)
            {
                Debug.LogError("match unsuccessful. Please write lyrics information to input file like \"2[0,1,3,4]Happy birthday to you\"");
                continue;
            }

            /* match.Success なら
             * 小節数: bar と 時刻比率List: ratioList を抽出 */
            // 表示「行」の小節数 `2` を bar に保存
            int barCount = int.Parse(match.Groups[1].Value);

            foreach (string timeRatio in match.Groups[2].Value.Split(','))
            {
                // パート開始タイミング `[0,1,3,4]` をリストに変換
                ratioList.Add(int.Parse(timeRatio));
            }
            // 残りの文字列 "Happy birthday to you" を歌詞として取得
            string lyrics = match.Groups[3].Value.Trim();

            // この歌詞行について part 情報をセット
            List<Part> partInfoList = SetPartListForThisLine(ratioList, barCount, lyrics, lineTiming);

            // lyricsList に追加
            _lyricsList.Add(new Line
            {
                timing = lineTiming,
                text = lyrics,
                partList = partInfoList
            });

            // 次の行の開始時刻計算
            lineTiming += _beat * barCount * _clock; // 6拍 (3拍子 * 2小節) * 0.5秒/拍 = 3秒
        }

        // 終了メッセージを追加
        //float endTime = lyricsStartTime + lines.Length * clock;
        _lyricsList.Add(new Line
        {
            timing = lineTiming,
            text = _eofText
        });
        _lyricsList.Add(new Line
        {
            timing = lineTiming + 2f,
            text = ""
        });

    }

    private List<Part> SetPartListForThisLine(List<int> ratioList, int barCount, string lyrics, float lineStartTime)
    {
        // パート色割り当て用
        List<int> order = CreateRandomNumList(_playerList.Count);
        int index = 0;

        /* LyricLineInfo の partList 情報生成*/
        List<Part> partInfoList = new List<Part>();
        //partInfoList = new List<LyricPartInfo>();

        // この行の歌詞を単語ごとに分割
        string[] wordList = lyrics.Split(' ');
        int i = 0;
        foreach (float timeRatio in ratioList)
        {
            float haku = _beat * barCount; // この行の総拍数
            float addSecond = timeRatio / haku;
            float partStartTime = lineStartTime + addSecond;
            //Debug.Log($"ratioList:{timeRatio}, partStartTime:{partStartTime}");

            /* generate part List */
            // lyrics(word) of this part
            string word = wordList[i];
            // if index out of range --> order をもう一度始めから回す
            if (index > order.Count) index = 0;
            // select a player from _playerList 
            Player player= _playerList[order[index]];

            //Debug.Log($"add _markDict: {Common.ToColorName(color)}, {mark}");

            // part 情報格納
            Part part = new Part
            {
                timing = partStartTime,
                word = word,
                player = player
            };

            partInfoList.Add(part);
            i++;
            index++;
        }

        return partInfoList;
    }

    List<int> CreateRandomNumList(int num)
    {
        List<int> resultList = new List<int>();
        List<int> candidateList = GenerateCandidateList(num);
        int maxLength = 20; // 作成するリストの長さ for parts(lyrics) of this line 
        int maxRepeats = 2; // 同じ数字が連続できる最大回数

        while (resultList.Count < maxLength)
        {
            int randomIndex = Random.Range(0, candidateList.Count);
            int selectedNum = candidateList[randomIndex];

            // 連続回数をチェック
            if (resultList.Count >= maxRepeats &&
                resultList[resultList.Count - 1] == selectedNum &&
                resultList[resultList.Count - 2] == selectedNum)
            {
                // 条件を満たさない場合は再選択
                continue;
            }

            // 条件を満たす場合、リストに追加
            resultList.Add(selectedNum);
        }

        return resultList;
    }

    List<int> GenerateCandidateList(int num)
    {
        List<int> candidateList = new List<int>();

        // 0から(num-1)までの整数をリストに追加
        for (int i = 0; i < num; i++)
        {
            candidateList.Add(i);
        }

        return candidateList;
    }

    int ParseMetaLine(string metaLine, string key)
    {
        // 指定されたキーの値を正規表現で取得
        Match match = Regex.Match(metaLine, $@"{key}\[(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
        {
            return value;
        }

        Debug.LogWarning($"Failed to parse {key} from: {metaLine}");
        return 0;
    }

    void ExportColorLog()
    {
        string logPath = Path.Combine(Application.dataPath, "ForHumanCheck.txt");
        using (StreamWriter writer = new StreamWriter(logPath))
        {
            writer.WriteLine("Lyrics Color Log:");
            foreach (Line thisLine in _lyricsList)
            {
                if (thisLine.text == "" || thisLine.text == _eofText)
                {
                    continue;
                }

                // XX.XX という形式で開始時刻をファイルに書き込み
                writer.WriteLine($"[{thisLine.timing:00.00}]");

                foreach (Part part in thisLine.partList)
                {
                    // パートの歌詞と色を出力
                    writer.WriteLine($"  \"{part.timing}: {part.word}\" - {Common.AvatarToLetter(part.player.avatar)} {Common.ToColorName(part.player.color)}, {part.player.color}");
                }
            }
        }
        Debug.Log($"Color log saved to {logPath}");
    }

    void ExportPartDivision()
    {
        string logPath = Path.Combine(Application.dataPath, FileName.CorrectPart);
        using (StreamWriter writer = new StreamWriter(logPath))
        {
            //writer.WriteLine("Part Log:");
            foreach (Line thisLine in _lyricsList)
            {
                if (thisLine.text == "" || thisLine.text == _eofText)
                {
                    continue;
                }

                // XX.XX という形式で開始時刻をファイルに書き込み
                //writer.WriteLine($"[{thisLine.startTime:00.00}] {thisLine.text}");

                foreach (Part part in thisLine.partList)
                {
                    // 誰のパートか？開始時間は？
                    string name = Common.ToColorName(part.player.color);
                    writer.WriteLine($"{part.timing:00.00}, {name}");
                }
            }
        }
        Debug.Log($"Color log saved to {logPath}");
    }

}
