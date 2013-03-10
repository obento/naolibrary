//
// obento_o@yahoo.co.jp
//


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using System.Globalization;

namespace NaoLibrary.Net
{
    public class Pop
    {
        private string hostname;
        private int port = 110;
        private TcpClient popTcp;
        private NetworkStream popStream;
        public static readonly string Terminate = "\r\n";
        private bool loggingMode = false;
        private LoginMode loginMode = LoginMode.PopLogin;
        private Socket popSocket = null;
        private int socketTimeOutMilliSecond = 30000;
        private bool closed = false;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="hostname">Popホストネーム</param>
        /// <param name="port">ポート番号</param>
        public Pop(string hostname, int port)
        {
            this.hostname = hostname;
            this.port = port;
        }

        /// <summary>
        /// ログイン
        /// </summary>
        /// <param name="user">ユーザー名</param>
        /// <param name="pass">パスワード</param>
        /// <returns>サーバからの応答</returns>
        public string Login(string user, string pass)
        {
            NetworkStream ns = this.PopStream;
            string mes = ReceiveMultiLineMessage(ns, true);
 
            if (this.Success(mes))
            {
                PopLogin poplgn = null;
                bool allclr = false;
                  
                if (this.LoginMode == LoginMode.UseApop)
                {
                    poplgn = new ApopLogin(this);
                    poplgn.Result = mes;
                    allclr = poplgn.Login(user, pass);
                }
                else if(this.LoginMode == LoginMode.PopLogin)
                {
                    poplgn = new PopLogin(this);
                    poplgn.Result = mes;
                    allclr = poplgn.Login(user, pass);
                }

                if (allclr)
                    return "+OK";
                else
                    return "-ERR";
            }
            else
            {
                Quit();
                throw new PopException(mes);
            }
        }

        /// <summary>
        /// ログアウト
        /// </summary>
        public string Logout()
        {
            string response = this.SendMessage(false, PopCommand.QUIT);
            if (!closed)
                PopSocket.Close();
            return response;
        }

        ///<summary>
        ///PopコマンドRETRでメールデータを読み込む
        ///</summary>
        ///<param name="messageNo">メッセージ番号</param>
        ///<returns>メール全文</returns>
        public string ReceiveRawMail(int messageNo)
        {
            try
            {
                string result = this.SendMessage(true, PopCommand.RETR, messageNo);
                
                if (Success(result))
                {
                    return result;
                }
                else
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new PopException("failed", ex);
            }
        }

        ///<summary>
        ///PopコマンドRETRからメールを読み込む
        ///</summary>
        ///<param name="messageNo">メッセージ番号</param>
        ///<returns>メール全文</returns>
        public PopMail ReceiveMail(int messageNo)
        {
            PopMail pm = new PopMail(this, messageNo);
            return pm;
        }


        /// <summary>
        /// Quit 内部でLogoutを呼ぶ
        /// </summary>
        public string Quit()
        {
            return Logout();
        }

        /// <summary>
        /// PopサーバからSTATを取得する
        /// </summary>
        /// <returns>サーバからの応答PopStateクラス</returns>
        public PopState State()
        {
            string response = this.SendMessage(false, PopCommand.STAT);

            if (this.Success(response))
            {
                string[] prm = response.Split(' ');
                return new PopState(prm[0], prm[1], prm[2]);
            }
            else
            {
                return new PopState(response);
            }
        }

        /// <summary>
        /// PopサーバからLISTを取得する
        /// </summary>
        /// <returns>サーバからの応答PopList配列</returns>
        public PopList[] List()
        {
            string response = this.SendMessage(true, PopCommand.LIST);

            string[] data = SplitString(response);
            List<PopList> lst = new List<PopList>();

            try
            {
                if (Success(response))
                {
                    for (int i = 1; data.Length > i; i++)
                    {
                        string[] param = data[i].Split(' ');
                        PopList pl = new PopList(Int32.Parse(param[0]), Int32.Parse(param[1]));
                        lst.Add(pl);
                    }
                }
                return lst.ToArray();
            }
            catch (Exception ex)
            {
                throw new PopException("failed", ex);
            }
        }

        /// <summary>
        /// PopサーバからLISTを取得する　オーバーロード
        /// </summary>
        /// <param name="number">メッセージ番号</param>
        /// <returns>サーバからの応答PopListクラス</returns>
        public PopList List(int number)
        {
            if (number < 1)
            {
                throw new PopException("failed", new ArgumentOutOfRangeException());
            }

            string response = this.SendMessage(false, PopCommand.LIST, number);

            if (this.Success(response))
            {
                string[] prm = response.Split(' ');
                PopList pl = new PopList(Int32.Parse(prm[1]), Int32.Parse(prm[2]));
                return pl;
            }
            else
                return PopList.Empty;
        }

        /// <summary>
        /// PopコマンドDELE
        /// </summary>
        /// <param name="number">メッセージ番号</param>
        /// <returns>成否のbool値</returns>
        public bool Delete(int number)
        {
            if (number < 1)
            {
                throw new PopException("failed", new ArgumentOutOfRangeException());
            }

            string response = this.SendMessage(false, PopCommand.DELE, number);

           return Success(response);
        }

        /// <summary>
        /// PopコマンドNOOP
        /// </summary>
        /// <returns>サーバからの応答</returns>
        public string Noop()
        {
            return this.SendMessage(false, PopCommand.NOOP);
        }

        /// <summary>
        /// PopコマンドRSET
        /// </summary>
        /// <returns>サーバからの応答</returns>
        public string Rest()
        {
            return this.SendMessage(false, PopCommand.RSET);
        }

        /// <summary>
        /// PopコマンドTOP
        /// </summary>
        /// <param name="num">メッセージ番号</param>
        /// <param name="lines">読みだす body の行数</param>
        /// <returns>エラーまたはヘッダ文字列</returns>
        public string[] Top(int num, int lines)
        {
            if (num < 1 || lines < 0)
            {
                throw new PopException("failed", new ArgumentOutOfRangeException());
            }
            return SplitString(this.SendMessage(true, PopCommand.TOP, num, lines));
        }

        /// <summary>
        /// UIDLを取得する
        /// </summary>
        /// <returns>UIDLのリスト</returns>
        public PopUIDL[] UIDL()
        {
            string response = this.SendMessage(true, PopCommand.UIDL);

            if (response == null)
                throw new PopException("failed", new NullReferenceException());

            List<PopUIDL> uidll = new List<PopUIDL>();
            int numCount = 1;
            StringReader sr = new StringReader(response);
            string line = sr.ReadLine();

            if (Success(line))
            {
                bool endMark = false;

                while (!endMark)
                {
                    line = sr.ReadLine();

                    if (String.IsNullOrEmpty(line) || line.StartsWith("."))
                    {
                        endMark = true;
                    }
                    else
                    {
                        string[] sAry = line.Split(' ');
                        PopUIDL uidl = null;

                        if (sAry.Length == 1)
                        {
                            uidl = new PopUIDL(Status.OK, numCount.ToString(), sAry[0]);
                        }
                        else if (sAry.Length == 2)
                        {
                            uidl = new PopUIDL(Status.OK, sAry[0], sAry[1]);
                        }
                        else
                        {
                            throw new PopException("UIDL failed");
                        }
                        numCount++;
                        uidll.Add(uidl);
                    }
                }

            }
            return uidll.ToArray();
        }

        /// <summary>
        /// UIDLを取得する　オーバーロード
        /// </summary>
        /// <param name="num">メッセージ番号</param>
        /// <returns>メッセージに対するUIDL</returns>
        public PopUIDL UIDL(int num)
        {
            if (num < 1)
                throw new PopException("failed", new ArgumentException());

            string response = this.SendMessage(false, PopCommand.UIDL);

            string[] lineArray = response.Split(' ');

            return PopUIDL.Parse(lineArray);
        }

        /// <summary>
        /// TOP コマンドで帰ってくるHEADコレクション
        /// </summary>
        /// <param name="messageNo">メッセージ番号</param>
        /// <returns>サーバから戻される GetHeaders コレクション</returns>
        public PopHeaders GetHeaders(int messageNo)
        {
            if (messageNo < 1)
                throw new PopException("failed", new ArgumentOutOfRangeException());

            string[] res = Top(messageNo, 0);

            PopHeaders headers = new PopHeaders();

            string line = res[0];

            if (!Success(line))
                return headers;

            string shead = "";
            string preshead = null;
            string sarg = "";
            int i = 0;

            for (int j = 1; res.Length > j; j++)
            {
                line = res[j];

                if (!String.IsNullOrEmpty(line))
                {
                    i = line.IndexOf(": ");
                    if (i > 0)
                    {
                        shead = line.Substring(0, i);
                        sarg = line.Substring(i + 2);
                        headers.AppendPopHead(shead, sarg);
                        preshead = shead;
                    }
                    else
                    {
                        headers.AppendPopHead(preshead, line);
                    }
                }
            }

            return headers;
        }

        /// <summary>
        /// PopCommand を受け付け リザルトを返す
        /// </summary>
        /// <param name="command">Popコマンド</param>
        /// <returns>サーバからの応答</returns>
        /*public string Command(string command)
        {
            return this.SendMessage(command);
        }*/

        /// <summary>
        /// ReceiveMultiLineMessageメソッド
        /// </summary>
        /// <returns>result</returns>
        internal string ReceiveMultiLineMessage(NetworkStream nstream, bool isSingleLine)
        {
            try
            {
                if (isSingleLine)
                {
                    string res = Pop.ReceiveLine(nstream).TrimEnd();
                    this.LoggingMessage("S: " + res);

                    return res;
                }
            }
            catch (Exception ex)
            {
                throw new PopException(ex.Message, ex);
            }

            try
            {
                StringBuilder sb = new StringBuilder();
                StringBuilder sbL = new StringBuilder();

                string line = Pop.ReceiveLine(nstream);

                while(line.TrimEnd() != ".")
                {
                    if (line.StartsWith(".."))
                    {
                        sb.Append(line.Substring(1));
                    }
                    else
                    {
                        sb.Append(line);
                    }
                    line = Pop.ReceiveLine(nstream);

                    this.LoggingMessage("S: " + line.TrimEnd());

                }
                string res = sb.ToString();

                return res;
            }
            catch (Exception ex)
            {
                throw new PopException(ex.Message, ex);
            }
        }

        /// <summary>
        /// from や subject で使う static な MIME パーサー
        /// </summary>
        /// <param name="rawSubject">入力値</param>
        /// <returns>戻り値</returns>
        public static string ParseMimeLine(string rawSubject)
        {
            Regex formater = new Regex(@"=\?(?<encoding>[\-a-zA-Z0-9]+)\?(?<format>[BQ])\?(?<data>[a-zA-Z0-9_\u001B\s\-\.\/\=\+\$\""\;\:\^\!\(\)\>\<\%\|\,\&\\\@\[\]\*\~\.\#\'`]+)\?=", RegexOptions.Compiled);

            StringBuilder sb = new StringBuilder();
            string[] lines = Pop.SplitFormat(rawSubject.Trim());

            foreach (string line in lines)
            {
                int idx = line.IndexOf("=?");
                if (idx >= 0 && line.LastIndexOf("?=") > (idx + 2))
                {
                    MatchCollection mc = formater.Matches(line);

                    if (mc.Count < 1)
                    {
                        sb.Append(line.Trim());
                        continue;
                    }

                    string encoding = mc[0].Groups["encoding"].Value;
                    string format = mc[0].Groups["format"].Value;
                    string rawText = mc[0].Groups["data"].Value;

                    byte[] buf = null;

                    if (format == "B")
                    {
                        buf = Convert.FromBase64String(rawText);
                        sb.Append(Encoding.GetEncoding(encoding).GetString(buf));
                    }
                    else if (format == "Q")
                    {
                        QuotedPrintable qp = new QuotedPrintable(encoding);

                        string qs = qp.DecodingString(rawText);

                        sb.Append(qs);
                    }
                    else
                    {
                        sb.Append(line);
                    }
                }
                else
                {
                    sb.Append(Encoding.Default.GetString(Encoding.GetEncoding("ISO-2022-JP").GetBytes(line)));
                }

            }
            /**/
            return sb.ToString();
        }

        /// <summary>
        /// from や subject 用の MIME static な Spliter
        /// </summary>
        /// <param name="lines">基になる文字列</param>
        /// <returns>string[] の戻り値</returns>
        public static string[] SplitFormat(string lines)
        {
            if (lines.IndexOf("?B?") >= 0)
                return SplitFormatForBEncoding(lines);
            else if (lines.IndexOf("?Q?") >= 0)
                return SplitFormatForQEncoding(lines);
            else
                return new string[] { lines };
        }

        /// <summary>
        /// B Encoding 用 SplitFormat
        /// </summary>
        /// <param name="lines">基になる文字列</param>
        /// <returns>string[] の戻り値</returns>
        private static string[] SplitFormatForBEncoding(string lines)
        {
            List<int> indexer = new List<int>();
            StringReader sr = new StringReader(lines.Trim());
            StringBuilder sb = new StringBuilder();
            string parts = string.Empty;
            string part = null;
            bool endMark = false;

            while (!endMark)
            {
                part = sr.ReadLine();
                if (String.IsNullOrEmpty(part))
                {
                    endMark = true;
                }
                else
                {
                    sb.Append(part.Trim());
                }
            }

            parts = sb.ToString();
            indexer.Add(0);
            int j = 0;
            part = null;
            for (int i = 0; parts.Length > i; )
            {
                if (parts.Length > i + 1)
                {
                    part = parts.Substring(i, 2);

                    if (part == "=?")
                    {
                        indexer.Add(i);
                        i += 2;
                        j = parts.IndexOf("?=", i);
                        if (j > 0)
                        {
                            i = j + 2;
                            indexer.Add(i);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    indexer.Add(parts.Length);
                    i++;
                }
            }


            List<string> ret = new List<string>();

            for (int i = 0; indexer.Count > i; i++)
            {
                if (indexer.Count > i + 1)
                {
                    int index1 = indexer[i];
                    int index2 = indexer[i + 1];
                    if (index1 <= index2)
                    {
                        if (index1 < index2)
                        {
                            part = parts.Substring(index1, index2 - index1);
                            ret.Add(part.Trim());
                        }
                        else if (index1 == index2)
                        { }
                        else
                        {
                            throw new PopException("Format error");
                        }
                    }
                    else
                    {
                        throw new PopException("Format error");
                    }
                }
            }
            return ret.ToArray();
        }


        /// <summary>
        /// Q Encoding 用 SplitFormat
        /// </summary>
        /// <param name="lines">基になる文字列</param>
        /// <returns>string[] の戻り値</returns>
        private static string[] SplitFormatForQEncoding(string lines)
        {
            List<int> indexer = new List<int>();
            StringReader sr = new StringReader(lines);
            StringBuilder sb = new StringBuilder();
            string parts = string.Empty;
            string part = null;
            bool endMark = false;

            while (!endMark)
            {
                part = sr.ReadLine();
                if (String.IsNullOrEmpty(part))
                {
                    endMark = true;
                }
                else
                {
                    sb.Append(part.Trim());
                }
            }

            parts = sb.ToString();
            indexer.Add(0);
            int j = 0;
            part = null;
            for (int i = 0; parts.Length > i; )
            {
                if (parts.Length > i + 1)
                {
                    part = parts.Substring(i, 2);

                    if (part == "=?")
                    {
                        indexer.Add(i);
                        i += 2;
                        int k = parts.IndexOf("?Q?", i); // middle point
                        i = k + 3;

                        j = parts.IndexOf("?=", i);
                        if (j > 0)
                        {
                            i = j + 2;
                            indexer.Add(i);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    indexer.Add(parts.Length);
                    i++;
                }
            }


            List<string> ret = new List<string>();

            for (int i = 0; indexer.Count > i; i++)
            {
                if (indexer.Count > i + 1)
                {
                    int index1 = indexer[i];
                    int index2 = indexer[i + 1];
                    if (index1 <= index2)
                    {
                        if (index1 < index2)
                        {
                            part = parts.Substring(index1, index2 - index1);
                            ret.Add(part.Trim());
                        }
                        else if (index1 == index2)
                        { }
                        else
                        {
                            throw new PopException("Format error");
                        }
                    }
                    else
                    {
                        throw new PopException("Format error");
                    }
                }
            }
            return ret.ToArray();
        }

        /// <summary>
        /// 内部で使う String 行列を改行で String[]に変換する
        /// </summary>
        /// <param name="inputText">入力データ</param>
        /// <returns>出力データ</returns>
        private string[] SplitString(string inputText)
        {
            StringReader sr = new StringReader(inputText);
            List<string> output = new List<string>();

            string line = sr.ReadLine();

            if (Success(line))
            {
                output.Add(line);
            }
            else
            {
                string[] ary = new string[1];
                ary[0] = line;
                return ary;
            }

            bool endMark = false;

            do
            {
                line = sr.ReadLine();
                if (line == null)
                {
                    return output.ToArray();
                }
                else if (line == "")
                {
                    output.Add(line);
                    continue;
                }
                else if (line.StartsWith(".."))
                {
                    output.Add(".");
                    continue;
                }
                else if (line == ".")
                {
                    return output.ToArray();
                }

                switch (line[0])
                {
                    case ' ':
                        output[output.Count - 1] = output[output.Count - 1] + Terminate + line;
                        break;
                    case '.':
                        endMark = true;
                        break;
                    case '\t':
                        output[output.Count - 1] = output[output.Count - 1] + Terminate + line;
                        break;
                    case '\0':
                        endMark = true;
                        break;
                    default:
                        output.Add(line);
                        break;
                }
            } while (!endMark);

            return output.ToArray();
        }

        /// <summary>
        /// Success
        /// </summary>
        /// <param name="message">サーバからの応答</param>
        /// <returns>+OK... または　-ERR...</returns>
        private bool Success(string message)
        {
            if (String.IsNullOrEmpty(message))
                return false;
            else
                return message.StartsWith(Status.OK);
        }

        /// <summary>
        /// LogginMessage  LoggingMode を監視してクライアント及びサーバのメッセージをConsoleに出力する
        /// </summary>
        /// <param name="message">出力するメッセージ</param>
        public void LoggingMessage(string message)
        {
            if (LoggingMode)
                Console.WriteLine(message);
        }

        /// <summary>
        /// HostName
        /// </summary>
        public string HostName
        {
            get
            {
                return this.hostname;
            }
        }

        /// <summary>
        /// Port
        /// </summary>
        public int Port
        {
            get { return this.port; }
        }

        /// <summary>
        /// UsingApop
        /// </summary>
        public LoginMode LoginMode
        {
            get
            {
                return loginMode;
            }
            set
            {
                loginMode = value;
            }
        }

        /// <summary>
        /// Socket がタイムアウトするまでの秒数
        /// </summary>
        public int SocketTimeOutMilliSecond
        {
            get
            {
                return socketTimeOutMilliSecond;
            }
            set
            {
                value = socketTimeOutMilliSecond;
            }
        }

        /// <summary>
        /// ログ出力モード
        /// </summary>
        public bool LoggingMode
        {
            get
            {
                return loggingMode;
            }
            set
            {
                loggingMode = value;
            }
        }

        /// <summary>
        /// 内部使用するTcpClient
        /// </summary>
        private TcpClient PopTcp
        {
            get
            {
                if (popTcp == null)
                {
                    try
                    {
                        popTcp = new TcpClient(HostName, Port);
                        return popTcp;
                    }
                    catch (Exception ex)
                    {
                        popTcp.Close();
                        throw new PopException("failed", ex);
                    }
                }
                else
                    return popTcp;
            }
        }

        /// <summary>
        /// 内部で使用するSocketクラス
        /// </summary>
        public Socket PopSocket
        {
            get
            {
                if (popSocket == null)
                {
                    popSocket = PopTcp.Client;
                    popSocket.ReceiveTimeout = this.SocketTimeOutMilliSecond;
                    popSocket.SendTimeout = this.SocketTimeOutMilliSecond;
                }
                return popSocket;
            }
        }

        /// <summary>
        /// 内部で使用するNetwrokStream
        /// </summary>
        private NetworkStream PopStream
        {
            get
            {
                if (popStream == null)
                {
                    return new NetworkStream(PopSocket);
                }
                return popStream;
            }
        }
        
        /// <summary>
        /// サーバの応答列挙体
        /// </summary>
        private class Status
        {
            public const string OK = "+OK";
            public const string ERR = "-ERR";
        }

        /// <summary>
        /// staticなNetworkStreamから1行読み込むメソッド
        /// </summary>
        /// <param name="stream">使用するNetworkStream</param>
        /// <returns>戻り値</returns>
        public static string ReceiveLine(NetworkStream stream)
        {
            bool stop = false;
            int length = 0;

            MemoryStream ms = new MemoryStream();

            int ch1 = stream.ReadByte();
            int ch2 = 0;
            
            while (!stop)
            {
                if (ch1 == 0x0d)
                {
                    ms.WriteByte((byte)ch1);
                    length++;

                    ch2 = stream.ReadByte();
                    if (ch2 == 0x0a)
                    {
                        ms.WriteByte((byte)ch2);
                        length++;
                        stop = true;
                    }
                    else
                    {
                        ch1 = ch2;
                    }

                }
                else
                {
                    ms.WriteByte((byte)ch1);
                    length++;

                    ch1 = stream.ReadByte();
                }
            }

            StringBuilder sb = new StringBuilder();

            sb.Append(Encoding.ASCII.GetString(ms.GetBuffer(), 0, length));

            return sb.ToString();
        }
       
        /// <summary>
        /// SendMessageメソッド サーバにメッセージを送る
        /// </summary>
        /// <param name="messages">任意のstring変数</param>
        /// <returns>result</returns>
        public string SendMessage(bool endOfComma, params object[] messages)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (object o in messages)
                {
                    sb.Append(o.ToString() + " ");
                }
                string message = sb.ToString().TrimEnd();

                this.LoggingMessage("C: " + message);
                this.PopSocket.Send(Encoding.ASCII.GetBytes(message + Pop.Terminate));
                NetworkStream ns = this.PopStream;
                return this.ReceiveMultiLineMessage(ns, !endOfComma);
            }
            catch (Exception ex)
            {
                throw new PopException("Send Message failed", ex);
            }
        }
    }

    /// <summary>
    /// Popコマンドの列挙体
    /// </summary>
    public class PopCommand
    {
        //The AUTHORIZATION State
        public const string STAT = "STAT";
        public const string LIST = "LIST";
        public const string RETR = "RETR";
        public const string DELE = "DELE";
        public const string NOOP = "NOOP";
        public const string RSET = "RSET";
        //The UPDATE State
        public const string QUIT = "QUIT";
        //Optional POP3 Commands
        public const string TOP = "TOP";
        public const string UIDL = "UIDL";
        public const string USER = "USER";
        public const string PASS = "PASS";
        public const string APOP = "APOP";
    }

    /// <summary>
    /// PopのLISTコマンドによるリザルトクラス
    /// </summary>
    public class PopList
    {
        private int messageNo;
        private int octets;
        public static PopList Empty = new PopList(0, 0);

        public PopList(int messageNo, int octets)
        {
            this.messageNo = messageNo;
            this.octets = octets;
        }

        public bool IsEmpty()
        {
            return this.MessageNo <= 0;
        }

        public int MessageNo
        {
            get
            {
                return messageNo;
            }
        }

        public int Octets
        {
            get
            {
                return octets;
            }
        }

    }

    /// <summary>
    /// PopのSTATコマンドによるリザルトクラス
    /// </summary>
    public class PopState
    {
        private string result;
        private int nn = 0;
        private int mm = 0;
        public static PopState Empty = new PopState("-ERR");

        public PopState(string message)
        {
            this.result = message;
        }

        public PopState(string result, string nn, string mm)
        {
            int n = 0;
            int m = 0;

            Int32.TryParse(nn, out n);
            Int32.TryParse(mm, out m);

            this.result = result;
            this.nn = n;
            this.mm = m;
        }

        public bool IsEmpty()
        {
            return result == "-ERR";
        }

        public string Result
        {
            get
            {
                return result;
            }
        }

        public int NN
        {
            get
            {
                return nn;
            }
        }

        public int MM
        {
            get
            {
                return mm;
            }
        }
    }

    /// <summary>
    /// PopのUIDLコマンドによるリザルトクラス
    /// </summary>
    public class PopUIDL
    {
        private string message;
        private string nummber;
        private string ID;
        public static PopUIDL Empty = new PopUIDL(string.Empty, "-1", "0");

        public PopUIDL(string message, string nummber, string ID)
        {
            this.message = message;
            this.nummber = nummber;
            this.ID = ID;
        }

        public bool IsEmpty()
        {
            return this.Message == String.Empty;
        }

        public string Message
        {
            get
            {
                return message;
            }
        }
        public string Nummber
        {
            get
            {
                return nummber;
            }
        }
        public string UIDL
        {
            get
            {
                return ID;
            }
        }

        /// <summary>
        /// UIDL parser
        /// </summary>
        /// <param name="seed">seed</param>
        /// <returns>returns</returns>
        public static PopUIDL Parse(string[] seed)
        {
            if (seed == null)
                throw new PopException("Parse error", new ArgumentException());
            PopUIDL idl = null;

            if (seed[0].StartsWith("+OK"))
            {
                idl = new PopUIDL(seed[0], seed[1], seed[2]);
            }
            if (seed[0].StartsWith("-ERR"))
            {
                idl = new PopUIDL(seed[0], "0", "0");
            }
            else
            {
                idl = new PopUIDL("+OK", seed[0], seed[1]);
            }

            return idl;
        }
    }

    /// <summary>
    /// TOP より Head を返すための Tableクラス
    /// </summary>
    public class PopHeaders
    {
        Dictionary<string, PopHead> headers = new Dictionary<string, PopHead>();

        public void AppendPopHead(string name, string value)
        {
            string unique = name.ToLower();
            if (headers.ContainsKey(unique))
            {
                PopHead ph = headers[unique];
                ph.AppendValue(value.Trim());
            }
            else
            {
                PopHead ph = new PopHead(name, value);
                this.headers.Add(unique, ph);
            }
        }

        /// <summary>
        /// PopHead
        /// </summary>
        /// <param name="name">Headr Name</param>
        /// <returns>Head の値</returns>
        public PopHead this[string name]
        {
            get
            {
                string uniqu = name.ToLower();
                bool exist = headers.ContainsKey(uniqu);
                if (exist)
                {
                    Dictionary<string, PopHead>.KeyCollection unqs = headers.Keys;
                    List<PopHead> ret = new List<PopHead>();

                    foreach (string s in unqs)
                    {
                        if (name.ToLower() == s)
                            return headers[s.ToLower()];
                    }
                    return PopHead.Empty;
                }
                else
                {
                    return PopHead.Empty;
                }
            }
        }

        public bool GetContainsKey(string head)
        {
            return headers.ContainsKey(head.ToLower());
        }


        public ICollection GetKeys()
        {
            return headers.Keys;
        }

        public ICollection GetValues()
        {
            return headers.Values;
        }
    }

    /// <summary>
    /// POP のヘッダを表すクラス
    /// </summary>
    public class PopHead
    {
        private List<string> value = new List<string>();
        private string name;
        public static readonly PopHead Empty = new PopHead(String.Empty, String.Empty);

        public PopHead(string name, string value)
        {
            this.name = name;
            this.value.Add(value);
        }

        public string Name
        {
            get
            {
                return name;
            }
        }

        public string Value
        {
            get
            {
                return this.ToString();
            }
        }

        public void AppendValue(string value)
        {
            this.value.Add(value);
        }

        public override string ToString()
        {
            return String.Join("\r\n", value.ToArray());
        }

        public bool IsEmpty()
        {
            return name == string.Empty;
        }
    }

    /// <summary>
    /// メール本文を返す PopMail クラス
    /// </summary>
    public class PopMail
    {
        private string subject = null;
        private string from;
        private string charset;
        private PopHeaders headers = null;
        private string result;
        private Pop parent = null;
        private int messageNo;

        public PopMail(Pop parent, int messageNo)
        {
            this.parent = parent;
            this.messageNo = messageNo;
        }

        public string GetHead(string name)
        {
            string uniqu = name.ToLower();
            bool exist = GetHeaders().GetContainsKey(uniqu);
            if (exist)
            {
                PopHead ph = Headers[uniqu];
                return ph.Value;
            }
            else
                return "";
        }

        public void SetHead(string name, string value)
        {
            string uniqu = name.ToLower();
            Headers.AppendPopHead(name, value);
        }

        private PopHeaders GetHeaders()
        {
            if (this.headers == null)
                this.headers = Parent.GetHeaders(messageNo);
            return this.headers;
        }

        public string Result
        {
            get
            {
                return result;
            }
        }

        public string From
        {
            get
            {
                if (from == null)
                {
                    PopHead ph = Headers["From"];
                    from = Pop.ParseMimeLine(ph.Value);
                }
                return from;
            }
        }

        public string Subject
        {
            get
            {
                if (subject == null)
                {
                    PopHead ph = Headers["Subject"];
                    subject = Pop.ParseMimeLine(ph.Value);
                }
                return subject;
            }
        }

        public Pop Parent
        {
            get
            {
                return this.parent;
            }
        }

        public PopHeaders Headers
        {
            get
            {
                return this.GetHeaders();
            }
        }

        public string ContentType
        {
            get
            {
                return Headers["Content-Type"].Value;
            }
        }

        public bool IsText
        {
            get
            {
                return ContentType.IndexOf("text") >= 0;
            }
        }

        public string Charset
        {
            get
            {
                if (charset != null)
                    return charset;

                if (IsText)
                {
                    int i = ContentType.IndexOf("=");
                    if (i >= 0)
                    {
                        //try
                        {
                            charset = ContentType.Substring(i + 1);

                            charset.TrimStart('"');
                            charset.TrimEnd('"');
                            return charset.ToUpper();
                        }
                        //catch (Exception ex)
                        //{

                        //    Console.WriteLine(ex + ",");
                        //}
                    }
                    return null;
                }
                else
                    return null;
            }
        }

        public string GetMail()
        {
            string response = Parent.ReceiveRawMail(this.messageNo);
            StringReader sr = new StringReader(response);
            StringBuilder sb = new StringBuilder();
            string line = null;
            string text;

            line = sr.ReadLine();

            result = line;

            do
            {
                line = sr.ReadLine();

            } while (!String.IsNullOrEmpty(line));

            while (line != null)
            {
                line = sr.ReadLine();
                if (line == ".")
                {
                    break;
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            text = sb.ToString().TrimEnd('\r', '\n');

            string ret = string.Empty;
            if (this.IsText)
            {
                byte[] buf = Encoding.ASCII.GetBytes(text);
                try
                {
                    ret = Encoding.GetEncoding(Charset).GetString(buf);
                }
                catch (ArgumentException)
                {

                    ret = Encoding.Default.GetString(buf);

                }
                catch
                {
                    ret = Encoding.Default.GetString(buf);
                }
                return ret;
            }
            else
            {
                return response;
            }
        }
    }

    /// <summary>
    /// Quoted-Printable class header 用
    /// </summary>
    public class QuotedPrintable
    {
        private string encoding;
        private static readonly string StartMark = "=1B";
        private static readonly string LastMark = "=1B(B";

        public QuotedPrintable(string encoding)
        {
            this.encoding = encoding;
        }

        /// <summary>
        /// Quoted-Printable を デコードする
        /// </summary>
        /// <param name="rawText">入力文字列</param>
        /// <returns>出力文字列</returns>
        public string DecodingString(string rawText)
        {
            StringBuilder qsb = new StringBuilder();

            string[] qs = this.Split(rawText);

            foreach (string s in qs)
            {
                string st = s.Trim();
                if (s.IndexOf(StartMark) >= 0)
                {
                    qsb.Append(this.InnnerDecodingString(st));
                }
                else
                {
                    qsb.Append(this.InnnerDecodingString(st));
                }
            }

            return qsb.ToString().Replace("_", " ");
        }

        private string[] SplitFormat(string lines)
        {
            List<int> indexer = new List<int>();
            StringReader sr = new StringReader(lines);
            string parts = null;
            string part = null;
            bool endMark = false;

            while (!endMark)
            {
                part = sr.ReadLine();
                if (String.IsNullOrEmpty(part))
                {
                    endMark = true;
                }
                else
                {
                    parts += part.Trim();
                }
            }

            indexer.Add(0);
            int j = 0;
            part = null;
            for (int i = 0; parts.Length > i; )
            {
                if (parts.Length > i + 2)
                {
                    part = parts.Substring(i, 3);

                    if (part == StartMark)
                    {
                        indexer.Add(i);
                        i += 3;
                        j = parts.IndexOf(LastMark, i);
                        if (j > 0)
                        {
                            i = j + 5;
                            indexer.Add(i);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    indexer.Add(parts.Length);
                    i++;
                }
            }


            List<string> ret = new List<string>();

            for (int i = 0; indexer.Count > i; i++)
            {
                if (indexer.Count > i + 1)
                {
                    int index1 = indexer[i];
                    int index2 = indexer[i + 1];
                    if (index1 <= index2)
                    {
                        if (index1 < index2)
                        {
                            part = parts.Substring(index1, index2 - index1);
                            ret.Add(part);
                        }
                        else if (index1 == index2)
                        { }
                        else
                        {
                            throw new PopException("Quoted-Printable Format error.");
                        }
                    }
                    else
                    {
                        throw new PopException("Quoted-Printable Format error.");
                    }
                }
            }
            return ret.ToArray();
        }

        private string[] Split(string value)
        {
            string[] splt = this.SplitFormat(value);
            return splt;
        }

        private string InnnerDecodingString(string value)
        {
            Regex regex = new Regex(@"\=[0-9a-fA-F][0-9a-fA-F]");

            if (value.StartsWith(StartMark))
            {
                StringBuilder sb = new StringBuilder();
                string val = regex.Replace(value, HexdecimelMatchEvaluator);

                value = val;
                byte[] buf = Encoding.ASCII.GetBytes(value);
                string ret = Encoding.GetEncoding(encoding).GetString(buf);
                return ret;
            }
            else
            {
                string val = regex.Replace(value, HexdecimelMatchEvaluator);
                return val;
            }
        }

        private string HexdecimelMatchEvaluator(Match m)
        {
            string left = m.Value.Substring(1, 2);
            int result = 0;
            if (Int32.TryParse(left, NumberStyles.AllowHexSpecifier, new CultureInfo("en-US"), out result))
            {
                return unchecked((char)result).ToString();
            }
            else
            {
                return m.Value;
            }
        }
    }

   ///
    ///<summary>標準popサーバへのログイン class</summary> 
    ///
    public class PopLogin
    {
        protected string result;
        protected Pop parent = null;

        public PopLogin(Pop parent)
        {
            this.parent = parent;
        }

        public virtual bool Login(string userName, string phase)
        {
            try
            {
                StringReader sReader = new StringReader(this.Result);
                string result = sReader.ReadLine();

                string response = parent.SendMessage(false, PopCommand.USER, userName);

                if (!response.StartsWith("+OK"))
                    return false;

                sReader = new StringReader(parent.SendMessage(false, PopCommand.PASS, phase));
                result = sReader.ReadLine();
                bool allClr = result.StartsWith("+OK");

                return allClr;
            }
            catch
            {
                return false;
            }
        }

        public string Result
        {
            get
            {
                return result;
            }
            set
            {
                result = value;
            }
        }
    }

    ///
    ///<summary>APOP 用のログインクラス</summary>
    ///
    public class ApopLogin : PopLogin
    {
        public ApopLogin(Pop parent) : base(parent)
        { }


        public override bool Login(string userName, string phase)
        {
            MD5 md5 = new MD5CryptoServiceProvider();
            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms);
            StringBuilder sb = new StringBuilder();
            sw.AutoFlush = true;
            sw.NewLine = Pop.Terminate;

            Match m = null;

            m = Regex.Match(this.Result, @"<.+>", RegexOptions.Compiled);

            if (m.Success)
            {
                string mc = m.Value;

                string svrDigest = m.Value;

                sw.Write(svrDigest + phase);

                byte[] buf = md5.ComputeHash(ms.ToArray());

                foreach (byte b in buf)
                {
                    sb.Append(b.ToString("x2"));
                }
                string digest = sb.ToString();

                this.Result = parent.SendMessage(false, PopCommand.APOP, userName, digest);

                return this.Result.StartsWith("+OK");
            }
            else
                throw new PopException("unable apop");
        }
    }


    [Flags]
    public enum LoginMode
    {
        err = -1,
        None = 0,
        PopLogin = 1,
        UseApop = 2
    }


    /// <summary>
    /// サーバエラーなどの例外クラス
    /// </summary>
    public class PopException : Exception
    {

        public PopException(string mes)
            : base(mes)
        { }

        public PopException(string mes, Exception ex)
            : base(mes, ex)
        { }
    }
}
