using System;
using System.Text;
using System.Collections.Generic;

namespace PlaywrightPrototype.models
{
    public class TestCase
    {
        public string name { get; set; }
        public string category { get; set; }       // e.g., "functional", "responsive", "accessibility"
        public List<TestStep> steps { get; set; }
        public string assertion { get; set; }
    }
}