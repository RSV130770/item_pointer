namespace LedMatrixControl
{
    public class TaskItem
    {
        public string Index { get; set; } = "";     // matches an LED label
        public string Name { get; set; } = "";
        public double Weight { get; set; }           // weight per unit
        public int Quantity { get; set; }
        public double? Precision { get; set; }        // optional per-row override (grams)
        public double? LengthMm { get; set; }          // optional, informational only

        public double ExpectedTotalWeight => Weight * Quantity;
    }
}
