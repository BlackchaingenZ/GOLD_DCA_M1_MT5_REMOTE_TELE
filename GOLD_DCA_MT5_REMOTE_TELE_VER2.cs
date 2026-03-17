//+------------------------------------------------------------------+
//|                                              XAUUSD_MT5_DCA.mq5  |
//|                                  Copyright 2026, Gemini AI Conv  |
//|                                             https://mql5.com     |
//+------------------------------------------------------------------+
#property copyright "Copyright 2026"
#property link      "https://mql5.com"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>

//========================
// INPUT PARAMETERS
//========================
input string TelegramBotToken = "8576224138:AAHKrCggXS05BSOg1OOpcUjyJvrQD4nIeVg";
input string TelegramChatID   = "@NGUYENEURO_PREMIUM";

//========================
// CONTROL TELEGRAM BOT
//========================
input string ControlBotToken = "8588800283:AAEQlpBLvwo6SUn-lDbMOnAeLLPT-ATWnKw";
input string ControlChatID   = "@REMOTEMT5";

datetime LastControlCheck = 0;
int ControlCheckInterval = 5;
int LastUpdateID = 0;

input string _1_ = "--- Indicator Settings ---";
input int    BB100 = 100;
input int    BB300 = 300;
input double BBDev = 2.0;       
input int    Stoch_K = 10;
input int    Stoch_Smooth = 6;

input string _2_ = "--- Money Management ---";
input double Lot1_Default = 0.2;
input double Lot2_Default = 0.4;
input double Lot3_Default = 0.8;

input string _3_ = "--- Trading Rules ---";
input int TP_Point_Default = 410;
input int SL_Point_Default = 1800;
input int Step_DCA_Default = 400;     
input int    MaxOrdersPerSide = 3;

input string _4_ = "--- Pro Filters ---";
input int    CooldownSeconds = 300; 
input int    MaxSpread = 80;
input int    ATR_Period = 14;
input double ATR_Min = 0.0008;

input int    Magic = 77777;
input int    Slippage = 40;

//========================
// GLOBAL VARIABLES
//========================
CTrade trade;
int handleBB100, handleBB300, handleStoch, handleATR;

datetime LastBuyTime = 0;
datetime LastSellTime = 0;
datetime LastBuyAlert  = 0;
datetime LastSellAlert = 0;
bool BlockBuyByTP = false;
bool BlockSellByTP = false;
bool IsBuySignalUsed = false;
bool IsSellSignalUsed = false;

int TP_Point;
int SL_Point;
int Step_DCA;

double Lot1;
double Lot2;
double Lot3;
bool EA_PAUSE = false;

// SIGNAL DENSITY MONITOR
datetime SignalTimes[3];
int SignalIndex = 0;
datetime LastClusterAlert = 0;
int ClusterWindowSeconds = 1000; 

//========================
// INITIALIZATION
//========================
int OnInit()
{
TP_Point = TP_Point_Default;
SL_Point = SL_Point_Default;
Step_DCA = Step_DCA_Default;

Lot1 = Lot1_Default;
Lot2 = Lot2_Default;
Lot3 = Lot3_Default;

   trade.SetExpertMagicNumber(Magic);
   
   // Khởi tạo Handles cho MT5
   handleBB100 = iBands(_Symbol, PERIOD_M1, BB100, 0, BBDev, PRICE_CLOSE);
   handleBB300 = iBands(_Symbol, PERIOD_M1, BB300, 0, BBDev, PRICE_CLOSE);
   handleStoch = iStochastic(_Symbol, PERIOD_M1, Stoch_K, Stoch_Smooth, Stoch_Smooth, MODE_SMA, STO_LOWHIGH);
   handleATR   = iATR(_Symbol, PERIOD_M1, ATR_Period);
   
   if(handleBB100 == INVALID_HANDLE || handleBB300 == INVALID_HANDLE || 
      handleStoch == INVALID_HANDLE || handleATR == INVALID_HANDLE)
   {
      Print("Lỗi khởi tạo chỉ báo!");
      return(INIT_FAILED);
   }
   
   return(INIT_SUCCEEDED);
}

//========================
// TELEGRAM FUNCTIONS
//========================
string UrlEncode(string str)
{
   string result = "";
   uchar array[];
   StringToCharArray(str, array);
   for(int i = 0; i < ArraySize(array)-1; i++)
   {
      uchar c = array[i];
      if((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
         c == '-' || c == '_' || c == '.' || c == '~')
         result += CharToString(c);
      else if(c == ' ')
         result += "%20";
      else
         result += "%" + StringFormat("%02X", c);
   }
   return result;
}

void SendTelegram(string message)
{
   string url = "https://api.telegram.org/bot"+TelegramBotToken+
                "/sendMessage?chat_id="+TelegramChatID+
                "&text="+UrlEncode(message);

   char data[];
   char result[];
   string headers;
   int res = WebRequest("GET", url, NULL, NULL, 5000, data, 0, result, headers);

   if(res == -1) Print("Telegram Error: ", GetLastError());
}

void SendControlTelegram(string message)
{
   string url = "https://api.telegram.org/bot"+ControlBotToken+
                "/sendMessage?chat_id="+ControlChatID+
                "&text="+UrlEncode(message);

   char data[];
   char result[];
   string headers;

   int res = WebRequest("GET", url, NULL, NULL, 5000, data, 0, result, headers);

   if(res == -1)
      Print("Control Telegram Error: ", GetLastError());
}

//========================
// HELPER FUNCTIONS (MT5 STYLE)
//========================
int CountOrders(ENUM_POSITION_TYPE type) {
   int c = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--) {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket)) {
         if(PositionGetInteger(POSITION_MAGIC) == Magic && PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_TYPE) == type) c++;
      }
   }
   return c;
}

double GetLastPrice(ENUM_POSITION_TYPE type) {
   datetime t = 0; double p = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--) {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket)) {
         if(PositionGetInteger(POSITION_MAGIC) == Magic && PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_TYPE) == type) {
            if(PositionGetInteger(POSITION_TIME) > t) { 
               t = (datetime)PositionGetInteger(POSITION_TIME); 
               p = PositionGetDouble(POSITION_PRICE_OPEN); 
            }
         }
      }
   }
   return p;
}

datetime GetFirstOpenTime(ENUM_POSITION_TYPE type) {
   datetime t = INT_MAX; bool found = false;
   for(int i = PositionsTotal()-1; i >= 0; i--) {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket)) {
         if(PositionGetInteger(POSITION_MAGIC) == Magic && PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_TYPE) == type) {
            if(PositionGetInteger(POSITION_TIME) < t) { 
               t = (datetime)PositionGetInteger(POSITION_TIME); 
               found = true; 
            }
         }
      }
   }
   return found ? t : 0;
}

bool CheckTpHit(ENUM_POSITION_TYPE type, datetime startTime) {
   if(startTime <= 0) return false;
   
   // Chọn lịch sử từ thời điểm lệnh đầu tiên mở đến hiện tại
   HistorySelect(startTime, TimeCurrent());
   
   for(int i = HistoryDealsTotal()-1; i >= 0; i--) {
      // Lấy ticket của deal thứ i
      ulong ticket = HistoryDealGetTicket(i); 
      
      if(ticket > 0) {
         // Trong MT5, phải truyền 'ticket' vào đầu mỗi hàm HistoryDeal...
         if(HistoryDealGetInteger(ticket, DEAL_MAGIC) == Magic && 
            HistoryDealGetString(ticket, DEAL_SYMBOL) == _Symbol && 
            HistoryDealGetInteger(ticket, DEAL_ENTRY) == DEAL_ENTRY_OUT) {
            
            ENUM_DEAL_TYPE dType = (type == POSITION_TYPE_BUY) ? DEAL_TYPE_SELL : DEAL_TYPE_BUY;
            
            if(HistoryDealGetInteger(ticket, DEAL_TYPE) == dType) {
               // Dòng 181 bị lỗi cũ nay đã thêm 'ticket' vào:
               double profit = HistoryDealGetDouble(ticket, DEAL_PROFIT);
               double swap   = HistoryDealGetDouble(ticket, DEAL_SWAP);
               double comm   = HistoryDealGetDouble(ticket, DEAL_COMMISSION);
               
               if(profit + swap + comm > 0) return true;
            }
         }
      }
   }
   return false;
}

void RegisterSignal() {
   SignalTimes[SignalIndex] = TimeCurrent();
   SignalIndex++;
   if(SignalIndex >= 3) {
      if(SignalTimes[2] - SignalTimes[0] <= ClusterWindowSeconds) {
         if(TimeCurrent() - LastClusterAlert > ClusterWindowSeconds) {
            SendTelegram("⚠️ CẢNH BÁO: 3 tín hiệu liên tiếp! Thị trường biến động mạnh.");
            LastClusterAlert = TimeCurrent();
         }
      }
      SignalTimes[0] = SignalTimes[1];
      SignalTimes[1] = SignalTimes[2];
      SignalIndex = 2;
   }
}

//========================
// MAIN EXECUTION
//========================
void OnTick() {
   CheckControlTelegram();
      if(EA_PAUSE)
   {
      Comment("EA STATUS: PAUSED");
      return;
   }
   double Ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double Bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   
   // 1. LẤY DỮ LIỆU CHỈ BÁO (CopyBuffer)
   double stochArr[], bb100L[], bb300L[], bb100U[], bb300U[], atrArr[];
   ArraySetAsSeries(stochArr, true); CopyBuffer(handleStoch, 0, 0, 1, stochArr);
   ArraySetAsSeries(bb100L, true);  CopyBuffer(handleBB100, 2, 0, 1, bb100L); // Lower
   ArraySetAsSeries(bb100U, true);  CopyBuffer(handleBB100, 1, 0, 1, bb100U); // Upper
   ArraySetAsSeries(bb300L, true);  CopyBuffer(handleBB300, 2, 0, 1, bb300L);
   ArraySetAsSeries(bb300U, true);  CopyBuffer(handleBB300, 1, 0, 1, bb300U);
   ArraySetAsSeries(atrArr, true);   CopyBuffer(handleATR, 0, 0, 1, atrArr);

   double stoch = stochArr[0];
   
   // --- LOGIC ---
   bool curBuySig  = (stoch <= 15 && bb100L[0] > bb300L[0]) || (stoch <= 15 && bb100L[0] < bb300L[0]); 
   bool curSellSig = (stoch >= 85 && bb100U[0] < bb300U[0]);

   // TELEGRAM ALERTS
   if(curBuySig && !IsBuySignalUsed && TimeCurrent()-LastBuyAlert >= CooldownSeconds) {
      string buyMsg = "🟢 MUA XAUUSD\nVùng: " + DoubleToString(Ask,2) + "\nSL: " + DoubleToString(Ask-SL_Point*_Point,2);
      SendTelegram(buyMsg);
      LastBuyAlert = TimeCurrent(); IsBuySignalUsed = true; RegisterSignal();
   }
   if(curSellSig && !IsSellSignalUsed && TimeCurrent()-LastSellAlert >= CooldownSeconds) {
      string sellMsg = "🔴 BÁN XAUUSD\nVùng: " + DoubleToString(Bid,2) + "\nSL: " + DoubleToString(Bid+SL_Point*_Point,2);
      SendTelegram(sellMsg);
      LastSellAlert = TimeCurrent(); IsSellSignalUsed = true; RegisterSignal();
   }

   int buyCount = CountOrders(POSITION_TYPE_BUY);
   int sellCount = CountOrders(POSITION_TYPE_SELL);

   // RESET LOGIC
   if(!curBuySig) IsBuySignalUsed = false;
   if(!curSellSig) IsSellSignalUsed = false;

   if(buyCount == 0) BlockBuyByTP = false;
   else if(!BlockBuyByTP) {
      if(CheckTpHit(POSITION_TYPE_BUY, GetFirstOpenTime(POSITION_TYPE_BUY))) BlockBuyByTP = true;
   }
   if(sellCount == 0) BlockSellByTP = false;
   else if(!BlockSellByTP) {
      if(CheckTpHit(POSITION_TYPE_SELL, GetFirstOpenTime(POSITION_TYPE_SELL))) BlockSellByTP = true;
   }

   // FILTERS
   double currentSpread = (Ask - Bid) / _Point;
   double currentATR = atrArr[0];
   bool spreadOK = (currentSpread <= MaxSpread);
   bool atrOK = (currentATR >= ATR_Min);

   // DASHBOARD
   Comment("Spread: ", currentSpread, (spreadOK?" OK":" HIGH"), "\nATR: ", currentATR, "\nBuy Count: ", buyCount, "\nSell Count: ", sellCount);

   if(!spreadOK || !atrOK) return;

//==============================
// 4. XỬ LÝ BUY
//==============================
if(!BlockBuyByTP && buyCount < MaxOrdersPerSide)
{
   if(buyCount == 0)
   {
      if(curBuySig && !IsBuySignalUsed && (TimeCurrent() - LastBuyTime >= CooldownSeconds))
      {
         if(trade.Buy(Lot1, _Symbol, Ask, 0, 0, "BUY_L1"))
         {
            LastBuyTime = TimeCurrent();
            IsBuySignalUsed = true;

            string msg =
            "EA OPEN BUY\n"
            "Symbol: " + _Symbol +
            "\nPrice: " + DoubleToString(Ask,_Digits) +
            "\nLot: " + DoubleToString(Lot1,2);

            SendControlTelegram(msg);
         }
      }
   }
   else
   {
      if(Ask <= GetLastPrice(POSITION_TYPE_BUY) - Step_DCA * _Point)
      {
         double lot = (buyCount == 1) ? Lot2 : Lot3;

         if(trade.Buy(lot, _Symbol, Ask, 0, 0, "BUY_DCA"))
            LastBuyTime = TimeCurrent();
      }
   }
}

//==============================
// 5. XỬ LÝ SELL
//==============================
if(!BlockSellByTP && sellCount < MaxOrdersPerSide)
{
   if(sellCount == 0)
   {
      if(curSellSig && !IsSellSignalUsed && (TimeCurrent() - LastSellTime >= CooldownSeconds))
      {
         if(trade.Sell(Lot1, _Symbol, Bid, 0, 0, "SELL_L1"))
         {
            LastSellTime = TimeCurrent();
            IsSellSignalUsed = true;

            string msg =
            "EA OPEN SELL\n"
            "Symbol: " + _Symbol +
            "\nPrice: " + DoubleToString(Bid,_Digits) +
            "\nLot: " + DoubleToString(Lot1,2);

            SendControlTelegram(msg);
         }
      }
   }
   else
   {
      if(Bid >= GetLastPrice(POSITION_TYPE_SELL) + Step_DCA * _Point)
      {
         double lot = (sellCount == 1) ? Lot2 : Lot3;

         if(trade.Sell(lot, _Symbol, Bid, 0, 0, "SELL_DCA"))
            LastSellTime = TimeCurrent();
      }
   }
}

ManageSLTP();
CheckCycleTP();
}

void ManageSLTP() {
   double fb = 0, fs = 0;
   datetime oldestBuyTime = 0, oldestSellTime = 0; // Biến để lưu thời gian lệnh cũ nhất

   // 1. Tìm GIÁ của lệnh mở ĐẦU TIÊN (Oldest Position)
   for(int i = PositionsTotal()-1; i >= 0; i--) {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket) && PositionGetInteger(POSITION_MAGIC) == Magic && PositionGetString(POSITION_SYMBOL) == _Symbol) {
         
         datetime posTime = (datetime)PositionGetInteger(POSITION_TIME);
         
         if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) {
            // So sánh thời gian với thời gian, không so sánh thời gian với giá fb
            if(oldestBuyTime == 0 || posTime < oldestBuyTime) {
               oldestBuyTime = posTime;
               fb = PositionGetDouble(POSITION_PRICE_OPEN);
            }
         }
         
         if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_SELL) {
            if(oldestSellTime == 0 || posTime < oldestSellTime) {
               oldestSellTime = posTime;
               fs = PositionGetDouble(POSITION_PRICE_OPEN);
            }
         }
      }
   }
   
   // 2. Thực hiện Modify SL/TP cho toàn bộ các vị thế
   for(int i = PositionsTotal()-1; i >= 0; i--) {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket) || PositionGetInteger(POSITION_MAGIC) != Magic || PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      
      double sl = 0, tp = 0;
      double entry = PositionGetDouble(POSITION_PRICE_OPEN);
      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);

      if(type == POSITION_TYPE_BUY && fb > 0) {
         sl = fb - SL_Point * _Point; 
         tp = entry + TP_Point * _Point;
      } 
      else if(type == POSITION_TYPE_SELL && fs > 0) {
         sl = fs + SL_Point * _Point; 
         tp = entry - TP_Point * _Point;
      }
      
      // Kiểm tra nếu giá trị SL/TP mới khác với giá trị hiện tại thì mới gửi lệnh Modify
      if(sl > 0) {
         double currentSL = PositionGetDouble(POSITION_SL);
         double currentTP = PositionGetDouble(POSITION_TP);
         
         if(MathAbs(currentSL - sl) > _Point || MathAbs(currentTP - tp) > _Point) {
            trade.PositionModify(ticket, NormalizeDouble(sl, _Digits), NormalizeDouble(tp, _Digits));
         }
      }
   }
}


void CheckCycleTP()
{
   // kiểm tra lịch sử deal gần nhất
   if(!HistorySelect(TimeCurrent()-300, TimeCurrent()))
      return;

   for(int i = HistoryDealsTotal()-1; i >= 0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);

      if(ticket == 0) continue;

      if(HistoryDealGetInteger(ticket, DEAL_MAGIC) != Magic) continue;
      if(HistoryDealGetString(ticket, DEAL_SYMBOL) != _Symbol) continue;

      // chỉ lấy deal đóng
      if(HistoryDealGetInteger(ticket, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;

      double profit = HistoryDealGetDouble(ticket, DEAL_PROFIT)
                    + HistoryDealGetDouble(ticket, DEAL_SWAP)
                    + HistoryDealGetDouble(ticket, DEAL_COMMISSION);

      // nếu là TP (có lời)
      if(profit > 0)
      {
         // đóng toàn bộ cycle
         for(int j = PositionsTotal()-1; j >= 0; j--)
         {
            ulong posTicket = PositionGetTicket(j);

            if(PositionSelectByTicket(posTicket))
            {
               if(PositionGetInteger(POSITION_MAGIC) == Magic &&
                  PositionGetString(POSITION_SYMBOL) == _Symbol)
               {
                  trade.PositionClose(posTicket);
               }
            }
         }

         return; // rất quan trọng
      }
   }
}
void CloseAllOrders()
{
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket = PositionGetTicket(i);

      if(PositionSelectByTicket(ticket))
      {
         if(PositionGetString(POSITION_SYMBOL)==_Symbol)
         {
            bool result = trade.PositionClose(ticket);

            if(!result)
               Print("Close error: ",GetLastError());
         }
      }
   }

   SendControlTelegram("REMOTE: Close all executed");
}

void CheckControlTelegram()
{
   if(ControlBotToken=="" || ControlChatID=="")
      return;

   if(TimeCurrent()-LastControlCheck<ControlCheckInterval)
      return;

   LastControlCheck=TimeCurrent();

   string url="https://api.telegram.org/bot"+ControlBotToken+
   "/getUpdates?offset="+IntegerToString(LastUpdateID+1);

   char data[];
   char result[];
   string headers;

   int res=WebRequest("GET",url,NULL,NULL,5000,data,0,result,headers);

   if(res==-1)
   {
      Print("Remote bot error: ",GetLastError());
      return;
   }

   string response=CharArrayToString(result);

   int pos=StringFind(response,"update_id");

   if(pos>=0)
   {
      string id=StringSubstr(response,pos+11,10);
      LastUpdateID=(int)StringToInteger(id);
   }
   // PAUSE EA
if(StringFind(response,"/pause")>=0)
{
   EA_PAUSE = true;
   SendControlTelegram("REMOTE: EA PAUSED");
}

// START EA
if(StringFind(response,"/start")>=0)
{
   EA_PAUSE = false;
   SendControlTelegram("REMOTE: EA STARTED");
}
   // CLOSE ALL
   if(StringFind(response,"/cls")>=0)
   {
      CloseAllOrders();
   }

   // LOT PRESETS
   if(StringFind(response,"/lot1")>=0)
   {
      Lot1=0.1;
      Lot2=0.2;
      Lot3=0.4;
      SendControlTelegram("REMOTE LOT: 0.1 - 0.2 - 0.4");
   }

   if(StringFind(response,"/lot2")>=0)
   {
      Lot1=0.2;
      Lot2=0.4;
      Lot3=0.8;
      SendControlTelegram("REMOTE LOT: 0.2 - 0.4 - 0.8");
   }

   if(StringFind(response,"/lot4")>=0)
   {
      Lot1=0.4;
      Lot2=0.8;
      Lot3=1.6;
      SendControlTelegram("REMOTE LOT: 0.4 - 0.8 - 1.6");
   }

   // SL
   if(StringFind(response,"/sl18")>=0)
   {
      SL_Point=1800;
      SendControlTelegram("REMOTE SL 18");
   }

   if(StringFind(response,"/sl20")>=0)
   {
      SL_Point=2000;
      SendControlTelegram("REMOTE SL 20");
   }

   if(StringFind(response,"/sl25")>=0)
   {
      SL_Point=2500;
      SendControlTelegram("REMOTE SL 25");
   }

   // TP
   if(StringFind(response,"/tp4")>=0)
   {
      TP_Point=400;
      SendControlTelegram("REMOTE TP 4");
   }

   if(StringFind(response,"/tp5")>=0)
   {
      TP_Point=500;
      SendControlTelegram("REMOTE TP 5");
   }

   if(StringFind(response,"/tp6")>=0)
   {
      TP_Point=600;
      SendControlTelegram("REMOTE TP 6");
   }

   // DCA
   if(StringFind(response,"/dca4")>=0)
   {
      Step_DCA=400;
      SendControlTelegram("REMOTE DCA 4");
   }

   if(StringFind(response,"/dca5")>=0)
   {
      Step_DCA=500;
      SendControlTelegram("REMOTE DCA 5");
   }

   if(StringFind(response,"/dca6")>=0)
   {
      Step_DCA=600;
      SendControlTelegram("REMOTE DCA 6");
   }
}