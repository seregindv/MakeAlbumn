namespace MakeAlbumn
{
    class City
    {
        public City(string name, int photoCount)
        {
            Name = name;
            PhotoCount = photoCount;
        }

        public string Name { set; get; }
        public int PhotoCount { set; get; }
    }
}
