using System;
using System.Text;
using ElmSharp;
using JuvoLogger.Tizen;
using JuvoLogger;
using Tizen;
using Tizen.Applications;
using XamarinPlayer.Services;
using System.Text.RegularExpressions;


namespace XamarinPlayer.Tizen
{
    

    class Program : global::Xamarin.Forms.Platform.Tizen.FormsApplication, IKeyEventSender, IPreviewPayloadEventSender
    {
        EcoreEvent<EcoreKeyEventArgs> _keyDown;
        private static ILogger Logger = LoggerManager.GetInstance().GetLogger("JuvoPlayer");
        public static readonly string Tag = "JuvoPlayer";

        protected override void OnCreate()
        {
            base.OnCreate();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            System.Net.ServicePointManager.DefaultConnectionLimit = 100;

            _keyDown = new EcoreEvent<EcoreKeyEventArgs>(EcoreEventType.KeyDown, EcoreKeyEventArgs.Create);
            _keyDown.On += (s, e) =>
            {
                // Send key event to the portable project using MessagingCenter                
                Xamarin.Forms.MessagingCenter.Send<IKeyEventSender, string>(this, "KeyDown", e.KeyName);
            };            
            
            LoadApplication(new App());
        }
               

        static void UnhandledException(object sender, UnhandledExceptionEventArgs evt)
        {
            if (evt.ExceptionObject is Exception e)
            {
                if (e.InnerException != null)
                    e = e.InnerException;

                Log.Error(Tag, e.Message);
                Log.Error(Tag, e.StackTrace);
            }
            else
            {
                Log.Error(Tag, "Got unhandled exception event: " + evt);
            }
        }

        protected override void OnAppControlReceived(AppControlReceivedEventArgs e)
        {
            // Handle the launch request, show the user the task requested through the "AppControlReceivedEventArgs" parameter
            // Smart Hub Preview function requires the below code to identify which deeplink have to be launched            
            ReceivedAppControl receivedAppControl = e.ReceivedAppControl;

            //fetch the JSON metadata defined on the smart Hub preview web server
            string payload = "";
            
            receivedAppControl.ExtraData.TryGet("PAYLOAD", out payload);
            Logger.Info("The PAYLOAD value: " + payload);

            if (string.IsNullOrEmpty(payload))
                return;

            //TODO
            var pattern = "";//   \{\"\w*\"\:\"[0-9]*\"\}*;
            string input = payload;
            Match m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            Logger.Info("The regexp result value: " + m.Value);
            if (!m.Success)
                return;

            payload = m.Value;
            Logger.Info("The PAYLOAD after regexp value: " + payload);
            //.WriteLine("Found '{0}' at position {1}.", m.Value, m.Index);

            //receivedAppControl.ExtraData.TryGet("payload", out payload);
            //Logger.Info("The payload value: " + payload);

            //receivedAppControl.ExtraData.TryGet("Payload", out payload);
            //Logger.Info("The Payload value: " + payload);

            ////Send key event to the portable project using MessagingCenter
            ////Logger.Info("The PAYLOAD value: " + payload);            
            Xamarin.Forms.MessagingCenter.Send<IPreviewPayloadEventSender, string>(this, "PayloadSent", payload);            

            base.OnAppControlReceived(e);
        }


        static void Main(string[] args)
        {
            TizenLoggerManager.Configure();
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            
            var app = new Program();
            
            global::Xamarin.Forms.Platform.Tizen.Forms.Init(app);            
            app.Run(args);            
        }
    }
}
