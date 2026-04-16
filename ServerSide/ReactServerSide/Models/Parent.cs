namespace ReactServerSide.Models
{
    public class Parent
    {
        int id;
        string email;
        string passwordhash;
        string firstname;
        string lastname;
        string phone;
        bool isactive;
        DateTime createdat;
        List<Children> children;

        public Parent(int id, string email, string passwordhash, string firstname, string lastname, string phone, bool isactive, DateTime createdat, List<Children> children)
        {
            this.id = id;
            this.email = email;
            this.passwordhash = passwordhash;
            this.firstname = firstname;
            this.lastname = lastname;
            this.phone = phone;
            this.isactive = isactive;
            this.createdat = createdat;
            this.children = children;
        }

        public int Id { get => id; set => id = value; }
        public string Email { get => email; set => email = value; }
        public string Passwordhash { get => passwordhash; set => passwordhash = value; }
        public string Firstname { get => firstname; set => firstname = value; }
        public string Lastname { get => lastname; set => lastname = value; }
        public string Phone { get => phone; set => phone = value; }
        public bool Isactive { get => isactive; set => isactive = value; }
        public DateTime Createdat { get => createdat; set => createdat = value; }
        public List<Children> Children { get => children; set => children = value; }
    }
}
