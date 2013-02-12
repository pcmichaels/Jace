﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jace.Execution;
using Jace.Operations;
using Jace.Tokenizer;
using Jace.Util;

namespace Jace
{
    /// <summary>
    /// The CalculationEngine class is the main class of Jace.NET to convert strings containing
    /// mathematical formulas into .NET Delegates and to calculate the result.
    /// It can be configured to run in a number of modes based on the constructor parameters choosen.
    /// </summary>
    public class CalculationEngine
    {
        private readonly IExecutor executor;
        private readonly Optimizer optimizer;
        private readonly CultureInfo cultureInfo;
        private readonly MemoryCache<string, Func<Dictionary<string, double>, double>> executionFormulaCache;
        private readonly IFunctionRegistry functionRegistry;
        private readonly bool cacheEnabled;
        private readonly bool optimizerEnabled;

        /// <summary>
        /// Creates a new instance of the <see cref="CalculationEngine"/> class with
        /// default parameters.
        /// </summary>
        public CalculationEngine()
            : this(CultureInfo.CurrentCulture, ExecutionMode.Compiled)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="CalculationEngine"/> class. The dynamic compiler
        /// is used for formula execution and the optimizer and cache are enabled.
        /// </summary>
        /// <param name="cultureInfo">
        /// The <see cref="CultureInfo"/> required for correctly reading floating poin numbers.
        /// </param>
        public CalculationEngine(CultureInfo cultureInfo)
            : this(cultureInfo, ExecutionMode.Compiled)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="CalculationEngine"/> class. The optimizer and 
        /// cache are enabled.
        /// </summary>
        /// <param name="cultureInfo">
        /// The <see cref="CultureInfo"/> required for correctly reading floating poin numbers.
        /// </param>
        /// <param name="executionMode">The execution mode that must be used for formula execution.</param>
        public CalculationEngine(CultureInfo cultureInfo, ExecutionMode executionMode)
            : this(cultureInfo, executionMode, true, true) 
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="CalculationEngine"/> class.
        /// </summary>
        /// <param name="cultureInfo">
        /// The <see cref="CultureInfo"/> required for correctly reading floating poin numbers.
        /// </param>
        /// <param name="executionMode">The execution mode that must be used for formula execution.</param>
        /// <param name="cacheEnabled">Enable or disable caching of mathematical formulas.</param>
        /// <param name="optimizerEnabled">Enable or disable optimizing of formulas.</param>
        public CalculationEngine(CultureInfo cultureInfo, ExecutionMode executionMode, bool cacheEnabled, bool optimizerEnabled)
        {
            this.executionFormulaCache = new MemoryCache<string, Func<Dictionary<string, double>, double>>();
            this.functionRegistry = new FunctionRegistry();
            this.cultureInfo = cultureInfo;
            this.cacheEnabled = cacheEnabled;
            this.optimizerEnabled = optimizerEnabled;

            if (executionMode == ExecutionMode.Interpreted)
                executor = new Interpreter();
            else if (executionMode == ExecutionMode.Compiled)
                executor = new DynamicCompiler();
            else
                throw new ArgumentException(string.Format("Unsupported execution mode \"{0}\".", executionMode), 
                    "executionMode");

            optimizer = new Optimizer(new Interpreter()); // We run the optimizer with the interpreter 

            // Register the default functions of Jace.NET into the function registry
            RegisterDefaultFunctions();
        }

        public double Calculate(string formulaText)
        {
            return Calculate(formulaText, new Dictionary<string, double>());
        }

        public double Calculate(string formulaText, Dictionary<string, double> variables)
        {
            if (string.IsNullOrEmpty(formulaText))
                throw new ArgumentNullException("formulaText");

            if (variables == null)
                throw new ArgumentNullException("variables");

            variables = ConvertVariableNamesToLowerCase(variables);
            VerifyVariableNames(variables);

            // Add the reserved variables to the dictionary
            variables.Add("e", Math.E);
            variables.Add("pi", Math.PI);

            if (IsInFormulaCache(formulaText))
            {
                Func<Dictionary<string, double>, double> formula = executionFormulaCache[formulaText];
                return formula(variables);
            }
            else
            {
                Operation operation = BuildAbstractSyntaxTree(formulaText);
                Func<Dictionary<string, double>, double> function = BuildFormula(formulaText, operation);

                return function(variables);
            }
        }

        public FormulaBuilder Formula(string formulaText)
        {
            if (string.IsNullOrEmpty(formulaText))
                throw new ArgumentNullException("formulaText");

            return new FormulaBuilder(formulaText, this);
        }

        /// <summary>
        /// Build a .NET func for the provided formula.
        /// </summary>
        /// <param name="formulaText">The formula that must be converted into a .NET func.</param>
        /// <returns>A .NET func for the provided formula.</returns>
        public Func<Dictionary<string, double>, double> Build(string formulaText)
        {
            if (string.IsNullOrEmpty(formulaText))
                throw new ArgumentNullException("formulaText");

            if (IsInFormulaCache(formulaText))
            {
                return executionFormulaCache[formulaText];
            }
            else
            {
                Operation operation = BuildAbstractSyntaxTree(formulaText);
                return BuildFormula(formulaText, operation);
            }
        }

        public void AddFunction(string functionName, Func<double> function)
        {
            functionRegistry.RegisterFunction(functionName, function);
        }
        
        public void AddFunction(string functionName, Func<double, double> function)
        {
            functionRegistry.RegisterFunction(functionName, function); 
        }

        public void AddFunction(string functionName, Func<double, double, double> function)
        {
            functionRegistry.RegisterFunction(functionName, function);
        }

        public void AddFunction(string functionName, Func<double, double, double, double> function)
        {
            functionRegistry.RegisterFunction(functionName, function);
        }

        public void AddFunction(string functionName, Func<double, double, double, double, double> function)
        {
            functionRegistry.RegisterFunction(functionName, function);
        }

        private void RegisterDefaultFunctions()
        {
            functionRegistry.RegisterFunction("sin", (Func<double, double>)((a) => Math.Sin(a)), false);
            functionRegistry.RegisterFunction("cos", (Func<double, double>)((a) => Math.Cos(a)), false);
            functionRegistry.RegisterFunction("csc", (Func<double, double>)((a) => MathUtil.Csc(a)), false);
            functionRegistry.RegisterFunction("sec", (Func<double, double>)((a) => MathUtil.Sec(a)), false);
            functionRegistry.RegisterFunction("asin", (Func<double, double>)((a) => Math.Asin(a)), false);
            functionRegistry.RegisterFunction("acos", (Func<double, double>)((a) => Math.Acos(a)), false);
            functionRegistry.RegisterFunction("tan", (Func<double, double>)((a) => Math.Tan(a)), false);
            functionRegistry.RegisterFunction("cot", (Func<double, double>)((a) => MathUtil.Cot(a)), false);
            functionRegistry.RegisterFunction("atan", (Func<double, double>)((a) => Math.Atan(a)), false);
            functionRegistry.RegisterFunction("acot", (Func<double, double>)((a) => MathUtil.Acot(a)), false);
            functionRegistry.RegisterFunction("loge", (Func<double, double>)((a) => Math.Log(a)), false);
            functionRegistry.RegisterFunction("log10", (Func<double, double>)((a) => Math.Log10(a)), false);
            functionRegistry.RegisterFunction("logn", (Func<double, double, double>)((a, b) => Math.Log(a, b)), false);
            functionRegistry.RegisterFunction("sqrt", (Func<double, double>)((a) => Math.Sqrt(a)), false);
            functionRegistry.RegisterFunction("abs", (Func<double, double>)((a) => Math.Abs(a)), false);
        }

        /// <summary>
        /// Build the abstract syntax tree for a given formula. The formula string will
        /// be first tokenized.
        /// </summary>
        /// <param name="formulaText">A string containing the mathematical formula that must be converted 
        /// into an abstract syntax tree.</param>
        /// <returns>The abstract syntax tree of the formula.</returns>
        private Operation BuildAbstractSyntaxTree(string formulaText)
        {
            TokenReader tokenReader = new TokenReader(cultureInfo);
            List<Token> tokens = tokenReader.Read(formulaText);

            AstBuilder astBuilder = new AstBuilder(functionRegistry);
            Operation operation = astBuilder.Build(tokens);

            if (optimizerEnabled)
                return optimizer.Optimize(operation, this.functionRegistry);
            else
                return operation;
        }

        private Func<Dictionary<string, double>, double> BuildFormula(string formulaText, Operation operation)
        {
            return executionFormulaCache.GetOrAdd(formulaText, v => executor.BuildFormula(operation, this.functionRegistry));
        }

        private bool IsInFormulaCache(string formulaText)
        {
            return cacheEnabled && executionFormulaCache.ContainsKey(formulaText);
        }

        /// <summary>
        /// Verify a collection of variables to ensure that all the variable names are valid.
        /// Users are not allowed to overwrite reserved variables or use function names as variables.
        /// If an invalid variable is detected an exception is thrown.
        /// </summary>
        /// <param name="variables">The colletion of variables that must be verified.</param>
        private void VerifyVariableNames(Dictionary<string, double> variables)
        {
            foreach (string variableName in variables.Keys)
            {
                if (EngineUtil.IsReservedVariable(variableName))
                    throw new ArgumentException(string.Format("The name \"{0}\" is a reservered variable name that cannot be overwritten.", variableName), "variables");

                if (EngineUtil.IsFunctionName(variableName))
                    throw new ArgumentException(string.Format("The name \"{0}\" is a restricted function name. Parameters cannot have this name.", variableName), "variables");
            }
        }

        private Dictionary<string, double> ConvertVariableNamesToLowerCase(Dictionary<string, double> variables)
        {
            Dictionary<string, double> temp = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> keyValuePair in variables)
            {
                temp.Add(keyValuePair.Key.ToLowerInvariant(), keyValuePair.Value);
            }

            return temp;
        }
    }
}
