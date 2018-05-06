﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ISAAR.MSolve.FEM.Embedding;
using System.Runtime.InteropServices;
using ISAAR.MSolve.Discretization.Interfaces;
using ISAAR.MSolve.FEM.Elements.SupportiveClasses;
using ISAAR.MSolve.Numerical.LinearAlgebra.Interfaces;
using ISAAR.MSolve.Numerical.LinearAlgebra;
using ISAAR.MSolve.FEM.Entities;
using ISAAR.MSolve.FEM.Interfaces;
using ISAAR.MSolve.Materials.Interfaces;

namespace ISAAR.MSolve.FEM.Elements
{
    public class Hexa8 : IStructuralFiniteElement, IEmbeddedHostElement
    {
        protected static double determinantTolerance = 0.00000001;
        protected static int iInt = 2;
        protected static int iInt2 = iInt * iInt;
        protected static int iInt3 = iInt2 * iInt;
        protected readonly static DOFType[] nodalDOFTypes = new DOFType[] { DOFType.X, DOFType.Y, DOFType.Z };
        protected readonly static DOFType[][] dofTypes = new DOFType[][] { nodalDOFTypes, nodalDOFTypes, nodalDOFTypes,
            nodalDOFTypes, nodalDOFTypes, nodalDOFTypes, nodalDOFTypes, nodalDOFTypes };
        protected readonly IFiniteElementMaterial3D[] materialsAtGaussPoints;
        protected IFiniteElementDOFEnumerator dofEnumerator = new GenericDOFEnumerator();

        #region Fortran imports
        [DllImport("femelements.dll",
            EntryPoint = "CALCH8GAUSSMATRICES",
            CallingConvention = CallingConvention.Cdecl)]
        protected static extern void CalcH8GaussMatrices(ref int iInt, [MarshalAs(UnmanagedType.LPArray)]double[,] faXYZ,
            [MarshalAs(UnmanagedType.LPArray)]double[] faWeight, [MarshalAs(UnmanagedType.LPArray)]double[,] faS,
            [MarshalAs(UnmanagedType.LPArray)]double[,] faDS, [MarshalAs(UnmanagedType.LPArray)]double[,,] faJ,
            [MarshalAs(UnmanagedType.LPArray)]double[] faDetJ, [MarshalAs(UnmanagedType.LPArray)]double[,,] faB);

        [DllImport("femelements.dll",
            EntryPoint = "CALCH8STRAINS",
            CallingConvention = CallingConvention.Cdecl)]
        protected static extern void CalcH8Strains(ref int iInt,
            [MarshalAs(UnmanagedType.LPArray)]double[,,] faB, [MarshalAs(UnmanagedType.LPArray)]double[] fau,
            [MarshalAs(UnmanagedType.LPArray)]double[,] faStrains);

        [DllImport("femelements.dll",
            EntryPoint = "CALCH8FORCES",
            CallingConvention = CallingConvention.Cdecl)]
        protected static extern void CalcH8Forces(ref int iInt,
            [MarshalAs(UnmanagedType.LPArray)]double[,,] faB, [MarshalAs(UnmanagedType.LPArray)]double[] faWeight,
            [MarshalAs(UnmanagedType.LPArray)]double[,] faStresses,
            [MarshalAs(UnmanagedType.LPArray)]double[] faForces);

        [DllImport("femelements.dll",
            EntryPoint = "CALCH8K",
            CallingConvention = CallingConvention.Cdecl)]
        protected static extern void CalcH8K(ref int iInt, [MarshalAs(UnmanagedType.LPArray)]double[,,] faE,
            [MarshalAs(UnmanagedType.LPArray)]double[,,] faB, [MarshalAs(UnmanagedType.LPArray)]double[] faWeight,
            [MarshalAs(UnmanagedType.LPArray)]double[] faK);

        [DllImport("femelements.dll",
            EntryPoint = "CALCH8MLUMPED",
            CallingConvention = CallingConvention.Cdecl)]
        protected static extern void CalcH8MLumped(ref int iInt, ref double fDensity,
            [MarshalAs(UnmanagedType.LPArray)]double[] faWeight, [MarshalAs(UnmanagedType.LPArray)]double[] faM);
        #endregion

        protected Hexa8()
        {
        }

        public Hexa8(IFiniteElementMaterial3D material)
        {
            materialsAtGaussPoints = new IFiniteElementMaterial3D[iInt3];
            for (int i = 0; i < iInt3; i++)
                materialsAtGaussPoints[i] = (IFiniteElementMaterial3D)material.Clone();
        }

        public Hexa8(IFiniteElementMaterial3D material, IFiniteElementDOFEnumerator dofEnumerator)
            : this(material)
        {
            this.dofEnumerator = dofEnumerator;
        }

        public IFiniteElementDOFEnumerator DOFEnumerator
        {
            get { return dofEnumerator; }
            set { dofEnumerator = value; }
        }

        public double Density { get; set; }
        public double RayleighAlpha { get; set; }
        public double RayleighBeta { get; set; }

        protected double[,] GetCoordinates(Element element)
        {
            double[,] faXYZ = new double[dofTypes.Length, 3];
            for (int i = 0; i < dofTypes.Length; i++)
            {
                faXYZ[i, 0] = element.Nodes[i].X;
                faXYZ[i, 1] = element.Nodes[i].Y;
                faXYZ[i, 2] = element.Nodes[i].Z;
            }
            return faXYZ;
        }

        protected double[,] GetCoordinatesTranspose(Element element)
        {
            double[,] faXYZ = new double[3, dofTypes.Length];
            for (int i = 0; i < dofTypes.Length; i++)
            {
                faXYZ[0, i] = element.Nodes[i].X;
                faXYZ[1, i] = element.Nodes[i].Y;
                faXYZ[2, i] = element.Nodes[i].Z;
            }
            return faXYZ;
        }

        #region IElementType Members

        public int ID
        {
            get { return 11; }
        }

        public ElementDimensions ElementDimensions
        {
            get { return ElementDimensions.ThreeD; }
        }

        public virtual IList<IList<DOFType>> GetElementDOFTypes(Element element)
        {
            return dofTypes;
        }

        public IList<Node> GetNodesForMatrixAssembly(Element element)
        {
            return element.Nodes;
        }

        private double[] CalcH8Shape(double fXi, double fEta, double fZeta)
        {
            const double fSqC125 = 0.5;
            double fXiP = (1.0 + fXi) * fSqC125;
            double fEtaP = (1.0 + fEta) * fSqC125;
            double fZetaP = (1.0 + fZeta) * fSqC125;
            double fXiM = (1.0 - fXi) * fSqC125;
            double fEtaM = (1.0 - fEta) * fSqC125;
            double fZetaM = (1.0 - fZeta) * fSqC125;

            return new double[] 
            {
                fXiM * fEtaM * fZetaM,
                fXiP * fEtaM * fZetaM,
                fXiP * fEtaP * fZetaM,
                fXiM * fEtaP * fZetaM,
                fXiM * fEtaM * fZetaP,
                fXiP * fEtaM * fZetaP,
                fXiP * fEtaP * fZetaP,
                fXiM * fEtaP * fZetaP
            };
        }

        private double[] CalcH8NablaShape(double fXi, double fEta, double fZeta)
        {
            const double fSq125 = 0.35355339059327376220042218105242;
            double fXiP = (1.0 + fXi) * fSq125;
	        double fEtaP = (1.0 + fEta) * fSq125;
	        double fZetaP = (1.0 + fZeta) * fSq125;
	        double fXiM = (1.0 - fXi) * fSq125;
	        double fEtaM = (1.0 - fEta) * fSq125;
	        double fZetaM = (1.0 - fZeta) * fSq125;

            double[] faDS = new double[24];
	        faDS[0] = - fEtaM * fZetaM;
	        faDS[1] = - faDS[0];
	        faDS[2] = fEtaP * fZetaM;
	        faDS[3] = - faDS[2];
	        faDS[4] = - fEtaM * fZetaP;
	        faDS[5] = - faDS[4];
	        faDS[6] = fEtaP * fZetaP;
	        faDS[7] = - faDS[6];
            faDS[8] = - fXiM * fZetaM;
            faDS[9] = - fXiP * fZetaM;
            faDS[10] = - faDS[9];
            faDS[11] = - faDS[8];
	        faDS[12] = - fXiM * fZetaP;
	        faDS[13] = - fXiP * fZetaP;
	        faDS[14] = - faDS[13];
	        faDS[15] = - faDS[12];
	        faDS[16] = - fXiM * fEtaM;
	        faDS[17] = - fXiP * fEtaM;
	        faDS[18] = - fXiP * fEtaP;
	        faDS[19] = - fXiM * fEtaP;
	        faDS[20] = - faDS[16];
	        faDS[21] = - faDS[17];
	        faDS[22] = - faDS[18];
            faDS[23] = - faDS[19];

            return faDS;
        }

        private Tuple<double[,], double[,], double> CalcH8JDetJ(double[,] faXYZ, double[] faDS)
        {
            double[,] faJ = new double[3, 3];
	        faJ[0, 0] = faDS[0] * faXYZ[0, 0] + faDS[1] * faXYZ[0, 1] + faDS[2] * faXYZ[0, 2] + faDS[3] * faXYZ[0, 3] + faDS[4] * faXYZ[0, 4] + faDS[5] * faXYZ[0, 5] + faDS[6] * faXYZ[0, 6] + faDS[7] * faXYZ[0, 7];
	        faJ[0, 1] = faDS[0] * faXYZ[1, 0] + faDS[1] * faXYZ[1, 1] + faDS[2] * faXYZ[1, 2] + faDS[3] * faXYZ[1, 3] + faDS[4] * faXYZ[1, 4] + faDS[5] * faXYZ[1, 5] + faDS[6] * faXYZ[1, 6] + faDS[7] * faXYZ[1, 7];
	        faJ[0, 2] = faDS[0] * faXYZ[2, 0] + faDS[1] * faXYZ[2, 1] + faDS[2] * faXYZ[2, 2] + faDS[3] * faXYZ[2, 3] + faDS[4] * faXYZ[2, 4] + faDS[5] * faXYZ[2, 5] + faDS[6] * faXYZ[2, 6] + faDS[7] * faXYZ[2, 7];
	        faJ[1, 0] = faDS[8] * faXYZ[0, 0] + faDS[9] * faXYZ[0, 1] + faDS[10] * faXYZ[0, 2] + faDS[11] * faXYZ[0, 3] + faDS[12] * faXYZ[0, 4] + faDS[13] * faXYZ[0, 5] + faDS[14] * faXYZ[0, 6] + faDS[15] * faXYZ[0, 7];
	        faJ[1, 1] = faDS[8] * faXYZ[1, 0] + faDS[9] * faXYZ[1, 1] + faDS[10] * faXYZ[1, 2] + faDS[11] * faXYZ[1, 3] + faDS[12] * faXYZ[1, 4] + faDS[13] * faXYZ[1, 5] + faDS[14] * faXYZ[1, 6] + faDS[15] * faXYZ[1, 7];
	        faJ[1, 2] = faDS[8] * faXYZ[2, 0] + faDS[9] * faXYZ[2, 1] + faDS[10] * faXYZ[2, 2] + faDS[11] * faXYZ[2, 3] + faDS[12] * faXYZ[2, 4] + faDS[13] * faXYZ[2, 5] + faDS[14] * faXYZ[2, 6] + faDS[15] * faXYZ[2, 7];
	        faJ[2, 0] = faDS[16] * faXYZ[0, 0] + faDS[17] * faXYZ[0, 1] + faDS[18] * faXYZ[0, 2] + faDS[19] * faXYZ[0, 3] + faDS[20] * faXYZ[0, 4] + faDS[21] * faXYZ[0, 5] + faDS[22] * faXYZ[0, 6] + faDS[23] * faXYZ[0, 7];
	        faJ[2, 1] = faDS[16] * faXYZ[1, 0] + faDS[17] * faXYZ[1, 1] + faDS[18] * faXYZ[1, 2] + faDS[19] * faXYZ[1, 3] + faDS[20] * faXYZ[1, 4] + faDS[21] * faXYZ[1, 5] + faDS[22] * faXYZ[1, 6] + faDS[23] * faXYZ[1, 7];
	        faJ[2, 2] = faDS[16] * faXYZ[2, 0] + faDS[17] * faXYZ[2, 1] + faDS[18] * faXYZ[2, 2] + faDS[19] * faXYZ[2, 3] + faDS[20] * faXYZ[2, 4] + faDS[21] * faXYZ[2, 5] + faDS[22] * faXYZ[2, 6] + faDS[23] * faXYZ[2, 7];

	        double fDet1 = faJ[0, 0] * (faJ[1, 1] * faJ[2, 2] - faJ[2, 1] * faJ[1, 2]);
	        double fDet2 =-faJ[0, 1] * (faJ[1, 0] * faJ[2, 2] - faJ[2, 0] * faJ[1, 2]);
	        double fDet3 = faJ[0, 2] * (faJ[1, 0] * faJ[2, 1] - faJ[2, 0] * faJ[1, 1]);
	        double fDetJ = fDet1 + fDet2 + fDet3;
	        if (fDetJ < determinantTolerance) 
                throw new ArgumentException(String.Format("Jacobian determinant is negative or under tolerance ({0} < {1}). Check the order of nodes or the element geometry.", fDetJ, determinantTolerance));

	        double fDetInv = 1.0 / fDetJ;
            double[,] faJInv = new double[3, 3];
	        faJInv[0, 0] = (faJ[1, 1] * faJ[2, 2] - faJ[2, 1] * faJ[1, 2]) * fDetInv;
	        faJInv[1, 0] = (faJ[2, 0] * faJ[1, 2] - faJ[1, 0] * faJ[2, 2]) * fDetInv;
            faJInv[2, 0] = (faJ[1, 0] * faJ[2, 1] - faJ[2, 0] * faJ[1, 1]) * fDetInv;
	        faJInv[0, 1] = (faJ[2, 1] * faJ[0, 2] - faJ[0, 1] * faJ[2, 2]) * fDetInv;
	        faJInv[1, 1] = (faJ[0, 0] * faJ[2, 2] - faJ[2, 0] * faJ[0, 2]) * fDetInv;
	        faJInv[2, 1] = (faJ[2, 0] * faJ[0, 1] - faJ[2, 1] * faJ[0, 0]) * fDetInv;
	        faJInv[0, 2] = (faJ[0, 1] * faJ[1, 2] - faJ[1, 1] * faJ[0, 2]) * fDetInv;
	        faJInv[1, 2] = (faJ[1, 0] * faJ[0, 2] - faJ[0, 0] * faJ[1, 2]) * fDetInv;
	        faJInv[2, 2] = (faJ[0, 0] * faJ[1, 1] - faJ[1, 0] * faJ[0, 1]) * fDetInv;

            return new Tuple<double[,], double[,], double>(faJ, faJInv, fDetJ);
        }

        //public virtual IMatrix2D<double> StiffnessMatrix(Element element)
        //{
        //    double[, ,] afE = new double[iInt3, 6, 6];
        //    for (int i = 0; i < iInt3; i++)
        //        for (int j = 0; j < 6; j++)
        //            for (int k = 0; k < 6; k++)
        //                afE[i, j, k] = ((Matrix2D<double>)materialsAtGaussPoints[i].ConstitutiveMatrix)[j, k];
        //    double[,] faXYZ = GetCoordinates(element);
        //    double[,] faDS = new double[iInt3, 24];
        //    double[,] faS = new double[iInt3, 8];
        //    double[, ,] faB = new double[iInt3, 24, 6];
        //    double[] faDetJ = new double[iInt3];
        //    double[, ,] faJ = new double[iInt3, 3, 3];
        //    double[] faWeight = new double[iInt3];
        //    double[] faK = new double[300];
        //    double[] test = new double[8];
        //    CalcH8GaussMatrices(ref iInt, faXYZ, faWeight, faS, faDS, faJ, faDetJ, faB);
        //    CalcH8K(ref iInt, afE, faB, faWeight, faK);
        //    return dofEnumerator.GetTransformedMatrix(new SymmetricMatrix2D<double>(faK));
        //}

        #region Alex code
        private double[,] CalculateDeformationMatrix(
            Jacobian3D jacobian, ShapeFunctionNaturalDerivatives3D[] shapeFunctionDerivatives)
        {
            double[,] jacobianInverse = jacobian.CalculateJacobianInverse();
            double[,] b = new double[8, 24];

            for (int shapeFunction = 0; shapeFunction < 8; shapeFunction++)
            {
                b[0, (3 * shapeFunction) + 0] = (jacobianInverse[0, 0] * shapeFunctionDerivatives[shapeFunction].Xi) +
                                                (jacobianInverse[0, 1] * shapeFunctionDerivatives[shapeFunction].Eta) +
                                                (jacobianInverse[0, 2] * shapeFunctionDerivatives[shapeFunction].Zeta);
                b[1, (3 * shapeFunction) + 1] = (jacobianInverse[1, 0] * shapeFunctionDerivatives[shapeFunction].Xi) +
                                                (jacobianInverse[1, 1] * shapeFunctionDerivatives[shapeFunction].Eta) +
                                                (jacobianInverse[1, 2] * shapeFunctionDerivatives[shapeFunction].Zeta);
                b[2, (3 * shapeFunction) + 2] = (jacobianInverse[2, 0] * shapeFunctionDerivatives[shapeFunction].Xi) +
                                                (jacobianInverse[2, 1] * shapeFunctionDerivatives[shapeFunction].Eta) +
                                                (jacobianInverse[2, 2] * shapeFunctionDerivatives[shapeFunction].Zeta);
                b[3, (3 * shapeFunction) + 0] = b[1, (3 * shapeFunction) + 1];
                b[3, (3 * shapeFunction) + 1] = b[0, (3 * shapeFunction) + 0];
                b[4, (3 * shapeFunction) + 1] = b[2, (3 * shapeFunction) + 2];
                b[4, (3 * shapeFunction) + 2] = b[1, (3 * shapeFunction) + 1];
                b[5, (3 * shapeFunction) + 0] = b[2, (3 * shapeFunction) + 2];
                b[5, (3 * shapeFunction) + 2] = b[0, (3 * shapeFunction) + 0];
            }

            return b;
        }

        private ShapeFunctionNaturalDerivatives3D[] CalculateShapeDerivativeValues(
            double xi, double eta, double zeta)
        {
            ShapeFunctionNaturalDerivatives3D[] shapeFunctionDerivatives =
                new ShapeFunctionNaturalDerivatives3D[8];
            for (int shapeFunction = 0; shapeFunction < 8; shapeFunction++)
            {
                shapeFunctionDerivatives[shapeFunction] = new ShapeFunctionNaturalDerivatives3D();
            }

            const double oneOverEight = 0.125;
            double xiPlus = 1.0 + xi;
            double etaPlus = 1.0 + eta;
            double zetaPlus = 1.0 + zeta;
            double xiMinus = 1.0 - xi;
            double etaMinus = 1.0 - eta;
            double zetaMinus = 1.0 - zeta;

            shapeFunctionDerivatives[0].Xi = -oneOverEight * etaMinus * zetaMinus;
            shapeFunctionDerivatives[1].Xi = -shapeFunctionDerivatives[0].Xi;
            shapeFunctionDerivatives[2].Xi = oneOverEight * etaPlus * zetaMinus;
            shapeFunctionDerivatives[3].Xi = -shapeFunctionDerivatives[2].Xi;
            shapeFunctionDerivatives[4].Xi = -oneOverEight * etaMinus * zetaPlus;
            shapeFunctionDerivatives[5].Xi = -shapeFunctionDerivatives[4].Xi;
            shapeFunctionDerivatives[6].Xi = oneOverEight * etaPlus * zetaPlus;
            shapeFunctionDerivatives[7].Xi = -shapeFunctionDerivatives[6].Xi;

            shapeFunctionDerivatives[0].Eta = -oneOverEight * xiMinus * zetaMinus;
            shapeFunctionDerivatives[1].Eta = -oneOverEight * xiPlus * zetaMinus;
            shapeFunctionDerivatives[2].Eta = -shapeFunctionDerivatives[1].Eta;
            shapeFunctionDerivatives[3].Eta = -shapeFunctionDerivatives[0].Eta;
            shapeFunctionDerivatives[4].Eta = -oneOverEight * xiMinus * zetaPlus;
            shapeFunctionDerivatives[5].Eta = -oneOverEight * xiPlus * zetaPlus;
            shapeFunctionDerivatives[6].Eta = -shapeFunctionDerivatives[5].Eta;
            shapeFunctionDerivatives[7].Eta = -shapeFunctionDerivatives[4].Eta;

            shapeFunctionDerivatives[0].Zeta = -oneOverEight * xiMinus * etaMinus;
            shapeFunctionDerivatives[1].Zeta = -oneOverEight * xiPlus * etaMinus;
            shapeFunctionDerivatives[2].Zeta = -oneOverEight * xiPlus * etaPlus;
            shapeFunctionDerivatives[3].Zeta = -oneOverEight * xiMinus * etaPlus;
            shapeFunctionDerivatives[4].Zeta = -shapeFunctionDerivatives[0].Zeta;
            shapeFunctionDerivatives[5].Zeta = -shapeFunctionDerivatives[1].Zeta;
            shapeFunctionDerivatives[6].Zeta = -shapeFunctionDerivatives[2].Zeta;
            shapeFunctionDerivatives[7].Zeta = -shapeFunctionDerivatives[3].Zeta;

            return shapeFunctionDerivatives;
        }

        private GaussLegendrePoint3D[] CalculateGaussMatrices(double[,] nodeCoordinates)
        {
            GaussLegendrePoint1D[] integrationPointsPerAxis =
                GaussQuadrature.GetGaussLegendrePoints(iInt);
            int totalSamplingPoints = (int)Math.Pow(integrationPointsPerAxis.Length, 3);

            GaussLegendrePoint3D[] integrationPoints = new GaussLegendrePoint3D[totalSamplingPoints];

            int counter = -1;
            foreach (GaussLegendrePoint1D pointXi in integrationPointsPerAxis)
            {
                foreach (GaussLegendrePoint1D pointEta in integrationPointsPerAxis)
                {
                    foreach (GaussLegendrePoint1D pointZeta in integrationPointsPerAxis)
                    {
                        counter += 1;
                        double xi = pointXi.Coordinate;
                        double eta = pointEta.Coordinate;
                        double zeta = pointZeta.Coordinate;

                        ShapeFunctionNaturalDerivatives3D[] shapeDerivativeValues =
                            this.CalculateShapeDerivativeValues(xi, eta, zeta);
                        Jacobian3D jacobian = new Jacobian3D(nodeCoordinates, shapeDerivativeValues);
                        double[,] deformationMatrix = this.CalculateDeformationMatrix(jacobian, shapeDerivativeValues);
                        double weightFactor = pointXi.WeightFactor * pointEta.WeightFactor * pointZeta.WeightFactor *
                                              jacobian.Determinant;

                        integrationPoints[counter] = new GaussLegendrePoint3D(
                            xi, eta, zeta, deformationMatrix, weightFactor);
                    }
                }
            }

            return integrationPoints;
        }

        public virtual IMatrix2D StiffnessMatrix(Element element)
        {
            double[,] coordinates = this.GetCoordinates(element);
            GaussLegendrePoint3D[] integrationPoints = this.CalculateGaussMatrices(coordinates);

            SymmetricMatrix2D stiffnessMatrix = new SymmetricMatrix2D(24);

            int pointId = -1;
            foreach (GaussLegendrePoint3D intPoint in integrationPoints)
            {
                pointId++;
                IMatrix2D constitutiveMatrix = materialsAtGaussPoints[pointId].ConstitutiveMatrix;
                double[,] b = intPoint.DeformationMatrix;
                for (int i = 0; i < 24; i++)
                {
                    double[] eb = new double[24];
                    for (int iE = 0; iE < 6; iE++)
                    {
                        eb[iE] = (constitutiveMatrix[iE, 0] * b[0, i]) + (constitutiveMatrix[iE, 1] * b[1, i]) +
                                 (constitutiveMatrix[iE, 2] * b[2, i]) + (constitutiveMatrix[iE, 3] * b[3, i]) +
                                 (constitutiveMatrix[iE, 4] * b[4, i]) + (constitutiveMatrix[iE, 5] * b[5, i]);
                    }

                    for (int j = i; j < 24; j++)
                    {
                        double stiffness = (b[0, j] * eb[0]) + (b[1, j] * eb[1]) + (b[2, j] * eb[2]) + (b[3, j] * eb[3]) +
                                           (b[4, j] * eb[4]) + (b[5, j] * eb[5]);
                        stiffnessMatrix[i, j] += stiffness * intPoint.WeightFactor;
                    }
                }
            }

            return stiffnessMatrix;
        }

        public IMatrix2D CalculateConsistentMass(Element element)
        {
            double[,] coordinates = this.GetCoordinates(element);
            GaussLegendrePoint3D[] integrationPoints = this.CalculateGaussMatrices(coordinates);

            SymmetricMatrix2D consistentMass = new SymmetricMatrix2D(24);

            foreach (GaussLegendrePoint3D intPoint in integrationPoints)
            {
                double[] shapeFunctionValues = this.CalcH8Shape(intPoint.Xi, intPoint.Eta, intPoint.Zeta);
                double weightDensity = intPoint.WeightFactor * this.Density;
                for (int shapeFunctionI = 0; shapeFunctionI < shapeFunctionValues.Length; shapeFunctionI++)
                {
                    for (int shapeFunctionJ = shapeFunctionI; shapeFunctionJ < shapeFunctionValues.Length; shapeFunctionJ++)
                    {
                        consistentMass[3 * shapeFunctionI, 3 * shapeFunctionJ] += shapeFunctionValues[shapeFunctionI] *
                                                                                  shapeFunctionValues[shapeFunctionJ] *
                                                                                  weightDensity;
                    }

                    for (int shapeFunctionJ = shapeFunctionI; shapeFunctionJ < shapeFunctionValues.Length; shapeFunctionJ++)
                    {
                        consistentMass[(3 * shapeFunctionI) + 1, (3 * shapeFunctionJ) + 1] =
                            consistentMass[3 * shapeFunctionI, 3 * shapeFunctionJ];

                        consistentMass[(3 * shapeFunctionI) + 2, (3 * shapeFunctionJ) + 2] =
                            consistentMass[3 * shapeFunctionI, 3 * shapeFunctionJ];
                    }
                }
            }

            return consistentMass;
        }

        #endregion

        public virtual IMatrix2D MassMatrix(Element element)
        {
            return CalculateConsistentMass(element);
        }

        public virtual IMatrix2D DampingMatrix(Element element)
        {
            var m = MassMatrix(element);
            var lc = m as ILinearlyCombinable;
            lc.LinearCombination(new double[] { RayleighAlpha, RayleighBeta }, new IMatrix2D[] { MassMatrix(element), StiffnessMatrix(element) });
            return m;
            //double[] faD = new double[300];
            //return new SymmetricMatrix2D<double>(faD);
        }

        public Tuple<double[], double[]> CalculateStresses(Element element, double[] localDisplacements, double[] localdDisplacements)
        {
            double[,] faXYZ = GetCoordinates(element);
            double[,] faDS = new double[iInt3, 24];
            double[,] faS = new double[iInt3, 8];
            double[, ,] faB = new double[iInt3, 24, 6];
            double[] faDetJ = new double[iInt3];
            double[, ,] faJ = new double[iInt3, 3, 3];
            double[] faWeight = new double[iInt3];
            double[,] fadStrains = new double[iInt3, 6];
            double[,] faStrains = new double[iInt3, 6];
            CalcH8GaussMatrices(ref iInt, faXYZ, faWeight, faS, faDS, faJ, faDetJ, faB);
            CalcH8Strains(ref iInt, faB, localDisplacements, faStrains);
            CalcH8Strains(ref iInt, faB, localdDisplacements, fadStrains);

            double[] dStrains = new double[6];
            double[] strains = new double[6];
            for (int i = 0; i < materialsAtGaussPoints.Length; i++)
            {
                for (int j = 0; j < 6; j++) dStrains[j] = fadStrains[i, j];
                for (int j = 0; j < 6; j++) strains[j] = faStrains[i, j];
                materialsAtGaussPoints[i].UpdateMaterial(dStrains);
            }

            return new Tuple<double[], double[]>(strains, materialsAtGaussPoints[materialsAtGaussPoints.Length - 1].Stresses);
        }

        public double[] CalculateForcesForLogging(Element element, double[] localDisplacements)
        {
            return CalculateForces(element, localDisplacements, new double[localDisplacements.Length]);
        }

        public double[] CalculateForces(Element element, double[] localTotalDisplacements, double[] localdDisplacements)
        {
            //Vector<double> d = new Vector<double>(localdDisplacements.Length);
            //for (int i = 0; i < localdDisplacements.Length; i++) 
            //    //d[i] = localdDisplacements[i] + localTotalDisplacements[i];
            //    d[i] = localTotalDisplacements[i];
            //double[] faForces = new double[24];
            //StiffnessMatrix(element).Multiply(d, faForces);

            double[,] faStresses = new double[iInt3, 6];
            for (int i = 0; i < materialsAtGaussPoints.Length; i++)
                for (int j = 0; j < 6; j++) faStresses[i, j] = materialsAtGaussPoints[i].Stresses[j];

            double[,] faXYZ = GetCoordinates(element);
            double[,] faDS = new double[iInt3, 24];
            double[,] faS = new double[iInt3, 8];
            double[, ,] faB = new double[iInt3, 24, 6];
            double[] faDetJ = new double[iInt3];
            double[, ,] faJ = new double[iInt3, 3, 3];
            double[] faWeight = new double[iInt3];
            double[] faForces = new double[24];
            CalcH8GaussMatrices(ref iInt, faXYZ, faWeight, faS, faDS, faJ, faDetJ, faB);
            CalcH8Forces(ref iInt, faB, faWeight, faStresses, faForces);

            return faForces;
        }

        public double[] CalculateAccelerationForces(Element element, IList<MassAccelerationLoad> loads)
        {
            Vector accelerations = new Vector(24);
            IMatrix2D massMatrix = MassMatrix(element);

            foreach (MassAccelerationLoad load in loads)
            {
                int index = 0;
                foreach (DOFType[] nodalDOFTypes in dofTypes)
                    foreach (DOFType dofType in nodalDOFTypes)
                    {
                        if (dofType == load.DOF) accelerations[index] += load.Amount;
                        index++;
                    }
            }
            double[] forces = new double[24];
            massMatrix.Multiply(accelerations, forces);
            return forces;
        }

        public void ClearMaterialState()
        {
            foreach (IFiniteElementMaterial3D m in materialsAtGaussPoints) m.ClearState();
        }

        public void SaveMaterialState()
        {
            foreach (IFiniteElementMaterial3D m in materialsAtGaussPoints) m.SaveState();
        }

        public void ClearMaterialStresses()
        {
            foreach (IFiniteElementMaterial3D m in materialsAtGaussPoints) m.ClearStresses();
        }

        #endregion

        #region IStructuralFiniteElement Members

        public bool MaterialModified
        {
            get
            {
                foreach (IFiniteElementMaterial3D material in materialsAtGaussPoints)
                    if (material.Modified) return true;
                return false;
            }
        }

        public void ResetMaterialModified()
        {
            foreach (IFiniteElementMaterial3D material in materialsAtGaussPoints) material.ResetModified();
        }

        #endregion

        #region IEmbeddedHostElement Members

        public EmbeddedNode BuildHostElementEmbeddedNode(Element element, Node node, IEmbeddedDOFInHostTransformationVector transformationVector)
        {
            var points = GetNaturalCoordinates(element, node);
            if (points.Length == 0) return null;

            element.EmbeddedNodes.Add(node);
            var embeddedNode = new EmbeddedNode(node, element, transformationVector.GetDependentDOFTypes);
            for (int i = 0; i < points.Length; i++)
                embeddedNode.Coordinates.Add(points[i]);
            return embeddedNode;
        }

        public double[] GetShapeFunctionsForNode(Element element, EmbeddedNode node)
        {
            double[,] elementCoordinates = GetCoordinatesTranspose(element);
            var shapeFunctions = CalcH8Shape(node.Coordinates[0], node.Coordinates[1], node.Coordinates[2]);
            var nablaShapeFunctions = CalcH8NablaShape(node.Coordinates[0], node.Coordinates[1], node.Coordinates[2]);
            var jacobian = CalcH8JDetJ(elementCoordinates, nablaShapeFunctions);

            return new double[]
            {
                shapeFunctions[0], shapeFunctions[1], shapeFunctions[2], shapeFunctions[3], shapeFunctions[4], shapeFunctions[5], shapeFunctions[6], shapeFunctions[7], 
                nablaShapeFunctions[0], nablaShapeFunctions[1], nablaShapeFunctions[2], nablaShapeFunctions[3], nablaShapeFunctions[4], nablaShapeFunctions[5], nablaShapeFunctions[6], nablaShapeFunctions[7], 
                nablaShapeFunctions[8], nablaShapeFunctions[9], nablaShapeFunctions[10], nablaShapeFunctions[11], nablaShapeFunctions[12], nablaShapeFunctions[13], nablaShapeFunctions[14], nablaShapeFunctions[15], 
                nablaShapeFunctions[16], nablaShapeFunctions[17], nablaShapeFunctions[18], nablaShapeFunctions[19], nablaShapeFunctions[20], nablaShapeFunctions[21], nablaShapeFunctions[22], nablaShapeFunctions[23], 
                jacobian.Item1[0, 0], jacobian.Item1[0, 1], jacobian.Item1[0, 2], jacobian.Item1[1, 0], jacobian.Item1[1, 1], jacobian.Item1[1, 2], jacobian.Item1[2, 0], jacobian.Item1[2, 1], jacobian.Item1[2, 2], 
                jacobian.Item2[0, 0], jacobian.Item2[0, 1], jacobian.Item2[0, 2], jacobian.Item2[1, 0], jacobian.Item2[1, 1], jacobian.Item2[1, 2], jacobian.Item2[2, 0], jacobian.Item2[2, 1], jacobian.Item2[2, 2] 
            };

            //double[] coords = GetNaturalCoordinates(element, node);
            //return CalcH8Shape(coords[0], coords[1], coords[2]);

            //double fXiP = (1.0 + coords[0]) * 0.5;
            //double fEtaP = (1.0 + coords[1]) * 0.5;
            //double fZetaP = (1.0 + coords[2]) * 0.5;
            //double fXiM = (1.0 - coords[0]) * 0.5;
            //double fEtaM = (1.0 - coords[1]) * 0.5;
            //double fZetaM = (1.0 - coords[2]) * 0.5;

            //return new double[] { fXiM * fEtaM * fZetaM,
            //    fXiP * fEtaM * fZetaM,
            //    fXiP * fEtaP * fZetaM,
            //    fXiM * fEtaP * fZetaM,
            //    fXiM * fEtaM * fZetaP,
            //    fXiP * fEtaM * fZetaP,
            //    fXiP * fEtaP * fZetaP,
            //    fXiM * fEtaP * fZetaP };
        }

        private double[] GetNaturalCoordinates(Element element, Node node)
        {
            double[] mins = new double[] { element.Nodes[0].X, element.Nodes[0].Y, element.Nodes[0].Z };
            double[] maxes = new double[] { element.Nodes[0].X, element.Nodes[0].Y, element.Nodes[0].Z };
            for (int i = 0; i < element.Nodes.Count; i++)
            {
                mins[0] = mins[0] > element.Nodes[i].X ? element.Nodes[i].X : mins[0];
                mins[1] = mins[1] > element.Nodes[i].Y ? element.Nodes[i].Y : mins[1];
                mins[2] = mins[2] > element.Nodes[i].Z ? element.Nodes[i].Z : mins[2];
                maxes[0] = maxes[0] < element.Nodes[i].X ? element.Nodes[i].X : maxes[0];
                maxes[1] = maxes[1] < element.Nodes[i].Y ? element.Nodes[i].Y : maxes[1];
                maxes[2] = maxes[2] < element.Nodes[i].Z ? element.Nodes[i].Z : maxes[2];
            }
            //return new double[] { (node.X - mins[0]) / ((maxes[0] - mins[0]) / 2) - 1,
            //    (node.Y - mins[1]) / ((maxes[1] - mins[1]) / 2) - 1,
            //    (node.Z - mins[2]) / ((maxes[2] - mins[2]) / 2) - 1 };

            bool maybeInsideElement = node.X <= maxes[0] && node.X >= mins[0] &&
                node.Y <= maxes[1] && node.Y >= mins[1] &&
                node.Z <= maxes[2] && node.Z >= mins[2];
            if (maybeInsideElement == false) return new double[0];

            const int jacobianSize = 3;
            const int maxIterations = 1000;
            const double tolerance = 1e-10;
            int iterations = 0;
            double deltaNaturalCoordinatesNormSquare = 100;
            double[] naturalCoordinates = new double[] { 0, 0, 0 };
            const double toleranceSquare = tolerance * tolerance;

            while (deltaNaturalCoordinatesNormSquare > toleranceSquare && iterations < maxIterations)
            {
                iterations++;
                var shapeFunctions = CalcH8Shape(naturalCoordinates[0], naturalCoordinates[1], naturalCoordinates[2]);
                double[] coordinateDifferences = new double[] { 0, 0, 0 };
                for (int i = 0; i < shapeFunctions.Length; i++)
                {
                    coordinateDifferences[0] += shapeFunctions[i] * element.Nodes[i].X;
                    coordinateDifferences[1] += shapeFunctions[i] * element.Nodes[i].Y;
                    coordinateDifferences[2] += shapeFunctions[i] * element.Nodes[i].Z;
                }
                coordinateDifferences[0] = node.X - coordinateDifferences[0];
                coordinateDifferences[1] = node.Y - coordinateDifferences[1];
                coordinateDifferences[2] = node.Z - coordinateDifferences[2];

                double[,] faXYZ = GetCoordinatesTranspose(element);
                double[] nablaShapeFunctions = CalcH8NablaShape(naturalCoordinates[0], naturalCoordinates[1], naturalCoordinates[2]);
                var inverseJacobian = CalcH8JDetJ(faXYZ, nablaShapeFunctions).Item2;

                double[] deltaNaturalCoordinates = new double[] { 0, 0, 0 };
                for (int i = 0; i < jacobianSize; i++)
                    for (int j = 0; j < jacobianSize; j++)
                        deltaNaturalCoordinates[i] += inverseJacobian[j, i] * coordinateDifferences[j];
                for (int i = 0; i < 3; i++)
                    naturalCoordinates[i] += deltaNaturalCoordinates[i];

                deltaNaturalCoordinatesNormSquare = 0;
                for (int i = 0; i < 3; i++)
                    deltaNaturalCoordinatesNormSquare += deltaNaturalCoordinates[i] * deltaNaturalCoordinates[i];
                //deltaNaturalCoordinatesNormSquare = Math.Sqrt(deltaNaturalCoordinatesNormSquare);
            }

            return naturalCoordinates.Count(x => Math.Abs(x) - 1.0 > tolerance) > 0 ? new double[0] : naturalCoordinates;
        }

        #endregion
    }
}
