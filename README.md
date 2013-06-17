naolibrary
==========

c# pop3 client


    class Program
    {
        static void Main(string[] args)
        {
            Pop pop = new Pop("pop.gmail.com", 995);
            
            pop.LoggingMode = false;
            pop.LoginMode = LoginMode.UsePop3s ;

            pop.Login("*******@gmail.com", "password");

            //Console.WriteLine(pop.SendMessage(true, "CAPA"));
            //Console.WriteLine(pop.SendMessage(false, "EXPIRE", "0"));
            
            PopState state = pop.State();
            
            PopUIDL[] idl = pop.UIDL();
            Console.WriteLine(idl.Length);

            PopList[] list = pop.List();

            for (int i = 1; list.Length>= i; i++)
            {
                Console.WriteLine(mail.Subject);
                string text = mail.GetMail();

                Console.WriteLine(i.ToString() + " " + text);
            }
            
            pop.Logout();

            Console.WriteLine("end");
            Console.ReadLine();
        }
    }

