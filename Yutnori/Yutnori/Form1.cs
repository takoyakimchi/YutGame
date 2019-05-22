/*
 * 2019-05-10
 * 완료된 것
 * <System> 메세지와 채팅 주고 받을 때 포맷에 맞는 출력(플레이어 번호, 닉네임)
 * 들어온 플레이어 순으로 플레이어 그림 배치(동기화 안됨)
 * 
 * 계획
 * 채팅창 스크롤 자동으로 내려오는 거 구현(강의자료에 있음)
 * 들어온 플레이어의 그림 동기화
 * 채팅창 플레이어별 색깔 부여
 * 
 * 오류
 * 클라이언트를 종료하면 서버가 종료되는 오류 존재
 * 클라이언트가 4명 이상 접속할 때 예외처리가 되는 지 모르겠음
 * 가끔 입력 형식이 이상하다고 에러뜸. -> 아직 무슨 에러인지 확인을 못함
 * 
 * 2019-05-22 구현계획
 * 혼자서라도 말을 움직일 수 있게 구현한 후, 여러 플레이어 구현
 * 준비 구현
 */

using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using System.Text;
using System.Net;
using System.Collections.Generic;

namespace Yutnori
{
    public partial class Form1 : Form
    {
        // 서버 클라이언트 공통 필드
        bool is_server = false;     // 현재 폼이 서버이면 true
        delegate void AppendTextDelegate(Control ctrl, string s);
        AppendTextDelegate text_appender;   // AppendText를 위한 대리자

        // 서버 측 필드
        Socket server_socket;       // 서버가 클라이언트들이 접속을 Listen하는 소켓
        IPAddress this_address;     // 서버의 IP주소 - 필요 없을 듯
        
        // 접속한 클라이언트들을 닉네임과 mapping해서 저장
        Dictionary<string, Socket> connected_clients = new Dictionary<string, Socket>();

        string server_nickname = "Server";  // 서버의 닉네임을 저장할 변수
        string client_nickname = "Guest";   // Accept한 시점의 클라이언트의 닉네임을 저장할 변수
        int player_count = 1;       // 접속한 플레이어 수. 초기값은 서버 혼자여서 1

        // 클라이언트 측 필드
        Socket client_socket;   // 클라이언트가 서버와 연결한 소켓
        string my_nickname = "Client";  // 클라이언트 자신의 닉네임
        int my_player_num = 1;     // 클라이언트 자신의 플레이어 번호
        
        PHASE phase = PHASE.LOGIN;           // 게임의 현재 페이즈

        enum PHASE //게임 페이즈
        {
            LOGIN,      //로그인화면
            LOBBY,      //대기실(게임 시작전 화면)
            MYTURN,     //당신의 턴
            NOT_MYTURN, //상대의 턴
            END_GAME    //게임종료
        }

        // 보조 기능 함수
        void AppendText(Control ctrl, string s)
        {// ctrl에 해당하는 컨트롤을 얻어와서 Text에 s를 삽입
            // Invoke가 필요한 컨트롤일 시 대리자를 통해 실행?
            if (ctrl.InvokeRequired) ctrl.Invoke(text_appender, ctrl, s);
            else
            {
                string source = ctrl.Text;
                ctrl.Text = source + Environment.NewLine + s;
            }
        }
        
        public void SendNickname()
        {// 서버에게 닉네임을 보내줌 - 클라이언트에서 사용
            // 서버가 대기 중인지 확인한다.
            if (!client_socket.IsBound)
            {
                MsgBoxHelper.Warn("닉네임 전송 : 서버가 실행되고 있지 않습니다!");
                return;
            }

            // 보낼 닉네임
            string nickname = my_nickname;
            //if (string.IsNullOrEmpty(nickname))
            //{
            //    MsgBoxHelper.Warn("닉네임을 입력하세요!");
            //    txt_nickname.Focus();
            //    return;
            //}

            // 문자열을 utf8 형식의 바이트로 변환한다.
            byte[] bDts = Encoding.UTF8.GetBytes(nickname);

            // 서버에 전송한다.
            client_socket.Send(bDts);
        }
        
        void GetNickname(Socket client)
        {// 닉네임 받기 - 서버에서 사용
            byte[] nickname_buffer = new byte[4096];

            // 서버는 클라이언트에게서 닉네임을 받고 다음 작업을 해야함.
            // -> 동기 함수인 Receive 호출
            // (서버는 클라이언트가 닉네임을보내즐 때 까지 기다린다.)
            client.Receive(nickname_buffer);

            // 서버에게 현재 클라이언트의 닉네임을 알려줌
            client_nickname = Encoding.UTF8.GetString(nickname_buffer).Trim('\0');
        }

        void SendPlayerNum()
        {// 클라이언트에게 플레이어 번호를 보내줌 - 서버에서 사용
            // 현재 클라이언트의 소켓을 가져온다.
            Socket socket = connected_clients[client_nickname];

            // 서버가 대기 중인지 확인한다.
            if (!socket.IsBound)
            {
                MsgBoxHelper.Warn("플레이어 번호 전송 : 서버가 실행되고 있지 않습니다!");
                return;
            }

            // 보낼 플레이어 번호
            int player_num = ++player_count;
            if (player_count > 4)   // -> 작동이 제대로 되는지 아직 모름.
            {
                MsgBoxHelper.Warn("방이 가득 찼습니다!");
                return;
            }

            // 문자열을 utf8 형식의 바이트로 변환한다.
            byte[] bDts = Encoding.UTF8.GetBytes(player_num.ToString());

            // 서버에 전송한다.
            socket.Send(bDts);
        }

        void GetPlayerNum()
        {//플레이어 번호 받기 - 클라이언트에서 사용
            byte[] player_num_buffer = new byte[4096];
            // 클라이언트는 서버로부터 플레이어 번호를
            // 할당 받은 후 다음 작업을 해야 함.
            // -> 동기 함수인 Receive 호출
            // (클라이언트는 서버가 플레이어 번호를 보낼 때 까지 기다림)
            client_socket.Receive(player_num_buffer);

            // 클라이언트에 서버로 부터 받은 플레이어 번호를 할당
            my_player_num = Int32.Parse(Encoding.UTF8.GetString(player_num_buffer).Trim('\0')); // error
        }

        // 폼 관련 함수
        public Form1()
        {
            InitializeComponent();
            // 서버, 클라이언트 소켓과 대리자 할당
            server_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            client_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            text_appender = new AppendTextDelegate(AppendText);
        }

        // 폼이 호출될 때
        private void Form1_Load(object sender, EventArgs e)
        {

            // IP 주소 가져오는건데 몰라도 됨. 안씀.
            IPHostEntry he = Dns.GetHostEntry(Dns.GetHostName());

            // 서버 측 코드
            // 처음으로 발견되는 ipv4 주소를 사용한다. -> 몰라도 됨. 안씀.
            foreach (IPAddress addr in he.AddressList)
            {
                if(addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    this_address = addr;
                    break;
                }
            }

            // 주소가 없다면..
            if (this_address == null)
                // 로컬 호스트 주소를 사용한다. -> Loopback == 127.0.0.1 == localhost
                this_address = IPAddress.Loopback;

            // ip 텍스트 박스에 지정한 값 대입
            txt_ip.Text = this_address.ToString();

            // 기본 포트 넣어줌
            txt_port.Text = "7777";

            // 클라이언트 측 코드
            // 처음으로 발견되는 ipv4 주소를 사용한다. -> 몰라도 됨.
            IPAddress default_host_address = null;
            foreach(IPAddress addr in he.AddressList)
            {
                if(addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    default_host_address = addr;
                    break;
                }
            }

            // 주소가 없다면..
            if (default_host_address == null)
                // 로컬 호스트 주소를 사용한다. -> Loopback == 127.0.0.1 == localhost
                default_host_address = IPAddress.Loopback;

            // ip 텍스트 박스에 지정한 값 대입
            txt_ip.Text = default_host_address.ToString();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {// 폼이 닫힐 때

            //if(!is_server)
            //    ServerStop();  //서버 중지
            //else
            //    Disconnect();  //연결 중지
        }
        
        private void btn_Host_Click(object sender, EventArgs e)
        {// 생성 버튼이 눌릴 때. 즉 서버가 만들어질 때.
            if (btn_Host.Text == "생성")
            {
                int port;
                // 포트 번호 예외처리
                if(!int.TryParse(txt_port.Text, out port))
                {
                    MsgBoxHelper.Error("포트 번호가 잘못 입력되었거나 입력되지 않았습니다.");
                    txt_port.Focus();
                    txt_port.SelectAll();
                    return;
                }

                is_server = true; // 생성 버튼을 눌렀으니 이 프로세스는 서버다.
                server_nickname = txt_nickname.Text;// 서버의 닉네임 가져옴.

                // 텍스트 박스에 서버(1P)의 접속을 써준다.
                AppendText(rtb_chat, string.Format("<System>{0}(1P)님이 입장하였습니다.", server_nickname));
                
                this.Invoke(new MethodInvoker(delegate ()
                {
                    //컨트롤 변형(채팅창을 사용가능하게 바꿔 줌)
                    lbl_loginAlert.ForeColor = Color.Black;
                    lbl_loginAlert.Text = "생성중...";

                    txt_ip.ReadOnly = true;
                    txt_port.ReadOnly = true;
                    txt_nickname.ReadOnly = true;
                    btn_Host.Enabled = false;
                    btn_Client.Enabled = false;
                }));
                
                // 서버에서 클라이언트의 연결 요청을 대기하기 위해
                // 소켓을 열어둔다.
                IPEndPoint server_ep = new IPEndPoint(IPAddress.Any, port);
                server_socket.Bind(server_ep);
                server_socket.Listen(3);    // 최대 3명의 클라이언트를 받는다.

                // 비동기적으로 클라이언트의 연결 요청을 받는다.
                // 연결 요청이 들어왔을 때 콜백인 AcceptCallback 함수가
                // 자동으로 호출 됨.
                server_socket.BeginAccept(AcceptCallback, null);

                //게임인터페이스로 이동
                pnl_login.Visible = false;  // 로그인 패널 끄기

                phase = PHASE.LOBBY;
                pb_chat.Image = img_pNumber.Images[0];
                pb_human1.Image = img_pHuman.Images[0];
                txt_send.Enabled = true;
                btn_send.Enabled = true;
                btn_ready.Enabled = true;

                //ServerStart();
                //m_thServer = new Thread(new ThreadStart(ServerStart));
                //m_thServer.Start();
            }
        }

        private void btn_Client_Click(object sender, EventArgs e)
        {
            if (btn_Client.Text == "입장")
            {
                if(txt_nickname.Text == "") {
                    MsgBoxHelper.Error("닉네임을 입력하세요!");
                    txt_nickname.Focus();
                    return;
                }

                if (client_socket.Connected)
                {
                    MsgBoxHelper.Error("이미 연결되어 있습니다!");
                    return;
                }

                int port;
                if(!int.TryParse(txt_port.Text, out port))
                {
                    MsgBoxHelper.Error("포트 번호가 잘못 입력되었거나 입력되지 않았습니다.");
                    txt_port.Focus();
                    txt_port.SelectAll();
                    return;
                }

                this.Invoke(new MethodInvoker(delegate ()
                {
                    // 컨트롤 변형
                    lbl_loginAlert.ForeColor = Color.Black;
                    lbl_loginAlert.Text = "호스트에 연결시도중...";

                    txt_ip.ReadOnly = true;
                    txt_port.ReadOnly = true;
                    txt_nickname.ReadOnly = true;
                    btn_Host.Enabled = false;

                    btn_Client.Enabled = false;
                    //btn_Client.Text = "취소";
                }));

                try
                {
                    client_socket.Connect(txt_ip.Text, port);
                }
                catch(Exception ex)
                {
                    MsgBoxHelper.Error("연결에 실패했습니다!\n오류 내용: {0}", MessageBoxButtons.OK, ex.Message);
                    return;
                }

                // 연결 완료되었다는 메세지를 띄워준다.
                //AppendText(rtb_chat, "서버와 연결되었습니다.");

                // 클라이언트 자신의 닉네임을 설정한다.
                my_nickname = txt_nickname.Text;

                // 서버에게 닉네임을 보낸다.
                SendNickname();

                // 서버에게서 플레이어 번호를 할당받는다.
                GetPlayerNum(); // error

                //게임인터페이스로 이동
                pnl_login.Visible = false;

                phase = PHASE.LOBBY;    // 로비로 이동.(준비 전 상태)

                // 플레이어별로 다른 이미지 표시
                switch(my_player_num)
                {
                    case 2:
                        pb_chat.Image = img_pNumber.Images[1];
                        pb_human2.Image = img_pHuman.Images[0];
                        break;
                    case 3:
                        pb_chat.Image = img_pNumber.Images[2];
                        pb_human3.Image = img_pHuman.Images[0];
                        break;
                    case 4:
                        pb_chat.Image = img_pNumber.Images[3];
                        pb_human4.Image = img_pHuman.Images[0];
                        break;
                }
                
                // 채팅창을 사용 가능하도록 변경
                txt_send.Enabled = true;
                btn_send.Enabled = true;
                btn_ready.Enabled = true;

                // 연결 완료, 서버에서 데이터가 올 수 있으므로 수신 대기한다.
                AsyncObject obj = new AsyncObject(4096);
                // 데이터 송신을 대기할 소켓 지정
                obj.WorkingSocket = client_socket;
                // 해당 소켓이 비동기로 데이터를 받는 함수 BeginReceive 호출
                client_socket.BeginReceive(obj.Buffer, 0, obj.BufferSize, 0, DataReceived, obj);
            }
            else
            {
                //Disconnect();
            }
        }
        
        // 메시지 전송을 담당하는 함수
        private void Send()
        {
            if(is_server)  // 함수를 호출한 프로세스가 서버일 경우
            {
                // 서버가 대기 중인지 확인한다.
                if(!server_socket.IsBound) {
                    MsgBoxHelper.Warn("서버가 실행되고 있지 않습니다!");
                    return;
                }

                // 보낼 텍스트 지정
                string tts = txt_send.Text.Trim();

                // 텍스트박스 예외처리
                //if(string.IsNullOrEmpty(tts)) {
                //    //MsgBoxHelper.Warn("텍스트가 입력되지 않았습니다!");
                //    txt_send.Focus();
                //    return;
                //}

                // 보낼 텍스트가 비어 있지 않다면
                if(!string.IsNullOrEmpty(tts)) {
                    // 서버의 닉네임과 메세지 및 플레이어 번호(1)를 전송한다.
                    // 문자열을 utf8 형식의 바이트로 변환한다. ('\x01'은 구분자.)
                    byte[] bDts = Encoding.UTF8.GetBytes(server_nickname + '\x01' + tts + '\x01' + "1");

                    // 연결된 모든 클라이언트에게 전송한다.
                    foreach(string nickname in connected_clients.Keys) {
                        Socket socket = connected_clients[nickname];
                        try {
                            socket.Send(bDts);
                        }
                        catch {
                            // 오류 발생하면 전송 취소하고 리스트에서 삭제한다.
                            try {
                                socket.Dispose();
                            }
                            catch {

                            }
                            connected_clients.Remove(nickname);
                        }
                    }

                    // 전송 완료 후 텍스트 박스에 추가하고, 원래의 내용은 지운다.
                    AppendText(rtb_chat, string.Format("(1P){0}: {1}", server_nickname.ToString(), tts));
                    txt_send.Clear();

                    // 커서를 텍스트박스로
                    txt_send.Focus();
                }
            }
            else  // 함수를 호출한 프로세스가 클라이언트이면
            {
                // 서버가 대기 중인지 확인한다.
                if(!client_socket.IsBound) {
                    MsgBoxHelper.Warn("서버가 실행되고 있지 않습니다!");
                    return;
                }

                // 보낼 텍스트 지정
                string tts = txt_send.Text.Trim();

                // 텍스트박스가 비어 있는 경우
                //if(string.IsNullOrEmpty(tts)) {
                //    //MsgBoxHelper.Warn("텍스트가 입력되지 않았습니다!");
                //    txt_send.Focus();
                //    return;
                //}

                // 보낼 텍스트가 비어 있지 않으면
                if(!string.IsNullOrEmpty(tts)) {
                    // 클라이언트의 닉네임과 메세지를 전송한다.
                    // 문자열을 utf8 형식의 바이트로 변환한다. ('\x01'은 구분자)
                    byte[] bDts = Encoding.UTF8.GetBytes(my_nickname + '\x01' + tts + '\x01' + my_player_num.ToString());

                    // 서버에 전송한다.
                    client_socket.Send(bDts);

                    // 전송 완료 후 텍스트 박스에 추가하고, 원래의 내용은 지운다.
                    AppendText(rtb_chat, string.Format("({0}P){1}: {2}", my_player_num, my_nickname, tts));
                    txt_send.Clear();

                    // 커서를 텍스트박스로
                    txt_send.Focus();
                }
            }
        }

        // Send 버튼을 눌렀을 때의 event handler
        private void btn_send_Click(object sender, EventArgs e)
        {// 데이터 전송 - 서버, 클라이언트 둘 다 사용
            Send(); // 메시지 전송
        }

        // 메시지 text box에서 엔터를 눌렀을 때의 event handler
        private void txt_send_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter) // 엔터가 눌리면
                Send(); // 메시지 전송
        }

        // 콜백 함수
        void AcceptCallback(IAsyncResult ar)
        {// 클라이언트의 접속 요청이 Accept 되었을 때 호출되는 콜백함수
            // 클라이언트의 연결 요청을 수락한다.
            Socket client = server_socket.EndAccept(ar);

            // 또 다른 클라이언트의 연결을 대기한다.
            server_socket.BeginAccept(AcceptCallback, null);

            // 비동기 통신을 위한 AsyncObject 객체 생성 및 소켓 지정
            AsyncObject obj = new AsyncObject(4096);
            obj.WorkingSocket = client;

            // 클라이언트의 닉네임을 얻는다.
            GetNickname(client);
            
            // 연결된 클라이언트 딕셔너리에 추가해준다.
            connected_clients.Add(client_nickname, client);

            // 클라이언트에게 플레이어 번호를 할당해준다.
            SendPlayerNum();

            // 모든 클라이언트에게 마지막 클라이언트가 연결되었다고 써준다.
            // 문자열을 utf8 형식의 바이트로 변환한다.
            byte[] bDts = Encoding.UTF8.GetBytes(string.Format("<System>{0}({1}P)님이 입장하였습니다.", client_nickname, player_count));

            // 연결된 모든 클라이언트에게 전송한다.
            //for(int i = connected_clients.Count - 1; i >= 0; i--)
            foreach (string nickname in connected_clients.Keys)
            {
                Socket socket = connected_clients[nickname];
                try
                {
                    socket.Send(bDts);
                }
                catch
                {
                    // 오류 발생하면 전송 취소하고 리스트에서 삭제한다.
                    try
                    {
                        socket.Dispose();
                    }
                    catch
                    {

                    }
                    connected_clients.Remove(nickname);
                }
            }

            // 전송 완료 후 텍스트 박스에 추가한다.
            AppendText(rtb_chat, string.Format("<System>{0}({1}P)님이 입장하였습니다.", client_nickname, player_count));

            // 클라이언트의 데이터를 받는다.
            client.BeginReceive(obj.Buffer, 0, 4096, 0, DataReceived, obj);
        }
        
        void DataReceived(IAsyncResult ar)
        {// 데이터 받기 - 서버, 클라이언트 둘 다 사용
            // BeginReceive에서 추가적으로 넘어온 데이터를 Asyncobject 형식으로 변환한다.
            AsyncObject obj = (AsyncObject)ar.AsyncState;

            // 데이터 수신을 끝낸다.
            int received = obj.WorkingSocket.EndReceive(ar); // 클라 끄면 서버 꺼지는 이유

            // 받은 데이터가 없으면(연결 끊어짐) 끝낸다.
            if (received <= 0)
            {
                obj.WorkingSocket.Close();
                return;
            }

            // 텍스트로 변환한다.
            string text = Encoding.UTF8.GetString(obj.Buffer);

            // 0x01 기준으로 자른다. == '\x01' 기준으로 자른다.
            // tokens[0] - 보낸 사람 닉네임
            // tokens[1] - 보낸 메세지
            // tokens[2] - 보낸 사람 플레이어 번호
            string[] tokens = text.Split('\x01');
            if(tokens.Length == 3)  // 토큰이 셋이면 채팅 메세지이다.
            {
                string nickname = tokens[0];
                string msg = tokens[1];
                string player_num = tokens[2].Trim('\0');

                // 텍스트 박스에 추가해준다.
                // (비동기식으로 작업하기 때문에 폼이 UI 스레드에서 작업을 해줘야 한다.
                // 따라서 대리자를 통해 처리한다.(Invoke))
                AppendText(rtb_chat, string.Format("({0}P){1}: {2}", player_num, nickname, msg));
            }
            else // 토큰이 하나이면 <System> 메세지이다.
            
            {
                string system_msg = text;
                // 텍스트 박스에 추가해준다.
                // (비동기식으로 작업하기 때문에 폼이 UI 스레드에서 작업을 해줘야 한다.
                // 따라서 대리자를 통해 처리한다.(Invoke))
                AppendText(rtb_chat, text);
            }

            if (is_server)  // 함수를 호출한 프로세스가 서버이면
            {
                // 모든 클라이언트에게 전송
                foreach (string nickname in connected_clients.Keys)
                {
                    Socket socket = connected_clients[nickname];
                    if (socket != obj.WorkingSocket)
                    {
                        try
                        {
                            socket.Send(obj.Buffer);
                        }
                        catch
                        {
                            // 오류 발생하면 전송 취소하고 리스트에서 삭제한다.
                            try
                            {
                                socket.Dispose();
                            }
                            catch
                            {

                            }
                            connected_clients.Remove(nickname);
                        }
                    }
                }

                // 데이터를 받은 후엔 다시 버퍼를 비워주고
                obj.ClearBuffer();

                // 같은 방법으로 수신을 대기한다.
                obj.WorkingSocket.BeginReceive(obj.Buffer, 0, 4096, 0, DataReceived, obj);
            }
            else
            {
                // 클라이언트에선 데이터를 전달해줄 필요가 없으므로 바로 수신 대기한다.
                // 데이터를 받은 후엔 다시 버퍼를 비워주고
                obj.ClearBuffer();

                // 같은 방법으로 수신을 대기한다.
                obj.WorkingSocket.BeginReceive(obj.Buffer, 0, 4096, 0, DataReceived, obj);
            }
        }

        
        // 기존 함수들 - 지워도 될 듯
        public void Message(string msg)
        {
            this.Invoke(new MethodInvoker(delegate ()
            {
                rtb_chat.AppendText(msg + "\r\n");
                //채팅 메세지중 '1P'~'4P'는 각각 색깔 배정하도록 하기
                //rtb_chat.Select()
                rtb_chat.Focus();
                rtb_chat.ScrollToCaret();
            }));
        }

        public void ServerStart()
        {/*
            int port;
            if (!int.TryParse(txt_port.Text, out port))
            {
                lbl_loginAlert.ForeColor = Color.Red;
                lbl_loginAlert.Text = "잘못된 PORT입니다.";

                txt_port.Focus();
                txt_port.SelectAll();
                return;
            }
            if(txt_nickname.Text == "")
            {
                lbl_loginAlert.ForeColor = Color.Red;
                lbl_loginAlert.Text = "닉네임을 입력하십시오.";

                txt_nickname.Focus();
                txt_nickname.SelectAll();
                return;
            }

            //컨트롤 변형
            lbl_loginAlert.ForeColor = Color.Black;
            lbl_loginAlert.Text = "생성중...";

            txt_ip.ReadOnly = true;
            txt_port.ReadOnly = true;
            txt_nickname.ReadOnly = true;
            btn_Host.Enabled = false;
            btn_Client.Enabled = false;

            // 서버에서 클라이언트의 연결 요청을 대기하기 위해
            // 소켓을 열어둔다.
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Any, port);
            mainSock.Bind(serverEP);
            mainSock.Listen(5);

            is_server_on = true;

            //게임인터페이스로 이동
            pnl_login.Visible = false;

            phase = PHASE.LOBBY;
            pb_chat.Image = img_pNumber.Images[0];
            pb_human1.Image = img_pHuman.Images[0];
            txt_send.Enabled = true;
            btn_send.Enabled = true;
            btn_ready.Enabled = true;
            Message("<System>" + txt_nickname.Text + "(" + 1 + "P)님이 입장하였습니다.");
       */
        }

        public void ServerStop()
        {/*
            if (!is_server_on)
                return;

            //m_listener.Stop();  //서버 소켓 작동 중지



            if (m_ThReader != null) m_ThReader.Abort(); //서버 소켓 스레드 종료
            if (m_thServer != null) m_thServer.Abort(); //스레드 종료

            //Message("서비스 종료");

            this.Invoke(new MethodInvoker(delegate ()
            {
                lbl_loginAlert.Text = "";

                txt_ip.ReadOnly = false;
                txt_port.ReadOnly = false;
                txt_nickname.ReadOnly = false;
                btn_Host.Enabled = true;
                btn_Client.Enabled = true;
            }));*/
        }

        public void Connect()
        {/*
            if (mainSock.Connected)
            {
                this.Invoke(new MethodInvoker(delegate ()
                {
                    lbl_loginAlert.ForeColor = Color.Red;
                    lbl_loginAlert.Text = "이미 연결되어 있습니다!";
                }));
                return;
            }

            int port;
            if (!int.TryParse(txt_port.Text, out port))
            {
                this.Invoke(new MethodInvoker(delegate ()
                {
                    lbl_loginAlert.ForeColor = Color.Red;
                    lbl_loginAlert.Text = "잘못된 PORT입니다.";

                    txt_port.Focus();
                    txt_port.SelectAll();
                }));
                return;
            }
            if (txt_nickname.Text == "")
            {
                this.Invoke(new MethodInvoker(delegate ()
                {
                    lbl_loginAlert.ForeColor = Color.Red;
                    lbl_loginAlert.Text = "닉네임을 입력하십시오.";

                    txt_nickname.Focus();
                    txt_nickname.SelectAll();
                }));
                return;
            }

            this.Invoke(new MethodInvoker(delegate ()
            {
                lbl_loginAlert.ForeColor = Color.Black;
                lbl_loginAlert.Text = "호스트에 연결시도중...";

                txt_ip.ReadOnly = true;
                txt_port.ReadOnly = true;
                txt_nickname.ReadOnly = true;
                btn_Host.Enabled = false;

                btn_Client.Enabled = false;
                //btn_Client.Text = "취소";
            }));

            try { mainSock.Connect(txt_ip.Text, port); }
            catch (Exception)
            {
                this.Invoke(new MethodInvoker(delegate ()
                {
                    lbl_loginAlert.ForeColor = Color.Red;
                    lbl_loginAlert.Text = "연결에 실패하였습니다!";

                    txt_ip.ReadOnly = false;
                    txt_port.ReadOnly = false;
                    txt_nickname.ReadOnly = false;
                    btn_Host.Enabled = true;

                    btn_Client.Enabled = true;
                    //btn_Client.Text = "입장";
                }));
                return;
            }
            
            // 연결 완료되었다는 메세지를 띄워준다.
            AppendText(txtHistory, "서버와 연결되었습니다.");

            // 연결 완료, 서버에서 데이터가 올 수 있으므로 수신 대기한다.
            AsyncObject obj = new AsyncObject(4096);
            obj.WorkingSocket = mainSock;
            mainSock.BeginReceive(obj.Buffer, 0, obj.BufferSize, 0, DataReceived, obj);
            //-----------
            m_Client = new TcpClient();
            try
            {
                //포트입력 예외처리
                int PORT = int.Parse(txt_port.Text);
                m_Client.Connect(txt_ip.Text, int.Parse(txt_port.Text));
            }
            catch
            {
                lbl_loginAlert.ForeColor = Color.Red;
                lbl_loginAlert.Text = "잘못된 IP 혹은 PORT입니다.";
                is_connected = false;
                return;
            }
            is_connected = true;

            

            m_Stream = m_Client.GetStream();

            m_Read = new StreamReader(m_Stream);
            m_Write = new StreamWriter(m_Stream);
            */
        }

        public void Disconnect()
        {
            /*if (mainSock.Connected) mainSock.Disconnect(true);

            if (m_thClient != null) m_thClient.Abort(); //스레드 종료
            this.Invoke(new MethodInvoker(delegate ()
            {
                lbl_loginAlert.Text = "";

                txt_ip.ReadOnly = false;
                txt_port.ReadOnly = false;
                txt_nickname.ReadOnly = false;
                btn_Host.Enabled = true;
                btn_Client.Text = "입장";
            }));*/
        }

        // 채팅창의 내용이 변경 시 event handler
        private void rtb_chat_TextChanged(object sender, EventArgs e)
        {
            // 스크롤을 자동적으로 맨 아래로 내린다.
            rtb_chat.SelectionStart = rtb_chat.Text.Length;
            rtb_chat.ScrollToCaret();
        }

        // "준비" 버튼 클릭 event handler
        private void btn_ready_Click(object sender, EventArgs e)
        {
            // 이미지 변경 부분
            switch(my_player_num) {
                // 서버
                case 1:
                    pb_human1.Image = img_pHuman.Images[1];
                    break;
                case 2:
                    pb_human2.Image = img_pHuman.Images[2];
                    break;
                case 3:
                    pb_human3.Image = img_pHuman.Images[3];
                    break;
                case 4:
                    pb_human4.Image = img_pHuman.Images[4];
                    break;
            }
        }
    }
}
