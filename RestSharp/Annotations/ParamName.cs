namespace Extensions.RestSharp.Annotations
{
    using System;

    /// <summary>
    /// Атрибут для пометки отправляемых параметров в обьекта
    /// </summary>
    public class ParamName : Attribute
    {
        public string Name { get; }

        /// <summary>
        /// Атрибут для пометки отправляемых параметров в обьекта
        /// </summary>
        /// <param name="name">Имя параметра для отправки на сервер</param>
        public ParamName(string name)
        {
            Name = name;
        }
    }
}