// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Funplay.Editor.Tools.Builtins;
using Funplay.Editor.Tools.Helpers;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Funplay.Editor.Tests
{
    public sealed class MultiTargetEditingTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Scene _scene;
        private bool _wasSceneDirty;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.GetActiveScene();
            _wasSceneDirty = _scene.IsValid() && _scene.isDirty;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            _objects.Clear();
            RestoreSceneDirtiness(_scene, _wasSceneDirty);
        }

        [TestCase("True", true)]
        [TestCase(" false ", false)]
        [TestCase("1", true)]
        [TestCase("0", false)]
        public void SetActive_ValidBooleanForms_AreApplied(string value, bool expected)
        {
            var go = CreateObject();
            go.SetActive(!expected);

            var result = GameObjectFunctions.SetActive(
                target: go.name,
                active: value,
                find_method: "by_name");

            AssertSuccess(result);
            Assert.That(go.activeSelf, Is.EqualTo(expected));
        }

        [Test]
        public void SetActive_InvalidBatchBoolean_DoesNotMutateTargets()
        {
            var first = CreateObject();
            var second = CreateObject();

            var result = GameObjectFunctions.SetActive(
                active: "tru",
                find_method: "by_name",
                targets: first.name + "," + second.name);

            AssertError(result, "INVALID_PARAM");
            Assert.That(first.activeSelf, Is.True);
            Assert.That(second.activeSelf, Is.True);
        }

        [Test]
        public void SetTransform_ConflictingTargetSelectors_DoNotMutateEitherObject()
        {
            var singleTarget = CreateObject();
            var batchTarget = CreateObject();

            var result = GameObjectFunctions.SetTransform(
                target: singleTarget.name,
                position: "9,0,0",
                find_method: "by_name",
                targets: batchTarget.name);

            AssertError(result, "INVALID_PARAM");
            Assert.That(singleTarget.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(batchTarget.transform.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void SetTransform_BatchTargets_AppliesValuesAndUndoRestoresAllTargets()
        {
            var first = CreateObject();
            var second = CreateObject();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();

            var result = GameObjectFunctions.SetTransform(
                position: "1.25,2.5,3.75",
                rotation: "10,20,30",
                scale: "2,3,4",
                find_method: "by_name",
                targets: first.name + "," + second.name);
            Undo.CollapseUndoOperations(undoGroup);

            AssertSuccess(result);
            Assert.That(first.transform.position, Is.EqualTo(new Vector3(1.25f, 2.5f, 3.75f)));
            Assert.That(second.transform.position, Is.EqualTo(new Vector3(1.25f, 2.5f, 3.75f)));
            Assert.That(first.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(second.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(GetProperty<int>(GetProperty<object>(result, "data"), "successCount"), Is.EqualTo(2));

            Undo.PerformUndo();
            Assert.That(first.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(second.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(first.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(second.transform.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void SetActive_BatchFindSpec_ResolvesAllInactiveMatches()
        {
            var sharedName = "MultiTargetEditing_Shared_" + Guid.NewGuid().ToString("N");
            var first = CreateObject();
            var second = CreateObject();
            first.name = sharedName;
            second.name = sharedName;
            first.SetActive(false);
            second.SetActive(false);

            var result = GameObjectFunctions.SetActive(
                active: "true",
                find_method: "by_name",
                targets: sharedName);

            AssertSuccess(result);
            Assert.That(first.activeSelf, Is.True);
            Assert.That(second.activeSelf, Is.True);
            Assert.That(GetProperty<int>(GetProperty<object>(result, "data"), "successCount"), Is.EqualTo(2));
        }

        [Test]
        public void SetComponentProperty_ConflictingTargetSelectors_DoNotMutateEitherObject()
        {
            var singleTarget = CreateObject();
            var batchTarget = CreateObject();
            var singleBody = singleTarget.AddComponent<Rigidbody>();
            var batchBody = batchTarget.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperty(
                target: singleTarget.name,
                component: "Rigidbody",
                property: "m_Mass",
                value: "9",
                find_method: "by_name",
                targets: batchTarget.name);

            AssertError(result, "INVALID_PARAM");
            Assert.That(singleBody.mass, Is.EqualTo(1f));
            Assert.That(batchBody.mass, Is.EqualTo(1f));
        }

        [Test]
        public void SetComponentProperty_SingleSerializedField_ReturnsNewValue()
        {
            var go = CreateObject();
            var body = go.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperty(
                target: go.name,
                component: "Rigidbody",
                property: "m_Mass",
                value: "3.5",
                find_method: "by_name");

            AssertSuccess(result);
            Assert.That(body.mass, Is.EqualTo(3.5f).Within(0.0001f));
            AssertReadBackValue(GetProperty<object>(GetProperty<object>(result, "data"), "newValue"), 3.5f);
        }

        [Test]
        public void SetComponentProperty_PublicNameMatchingSerializedField_ReturnsNewValue()
        {
            var go = CreateObject();
            var body = go.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperty(
                target: go.name,
                component: "Rigidbody",
                property: "isKinematic",
                value: "true",
                find_method: "by_name");

            AssertSuccess(result);
            Assert.That(body.isKinematic, Is.True);
            var newValue = GetProperty<object>(GetProperty<object>(result, "data"), "newValue");
            Assert.That(newValue, Is.Not.Null);
            Assert.That(GetProperty<object>(newValue, "value"), Is.EqualTo(true));
        }

        [Test]
        public void SetComponentProperty_ReflectionOnlyMember_ReturnsNewValue()
        {
            var go = CreateObject();

            var result = ComponentPropertyFunctions.SetComponentProperty(
                target: go.name,
                component: "Transform",
                property: "position",
                value: "{\"x\":1.25,\"y\":2.5,\"z\":3.75}",
                find_method: "by_name");

            AssertSuccess(result);
            Assert.That(go.transform.position, Is.EqualTo(new Vector3(1.25f, 2.5f, 3.75f)));
            var newValue = GetProperty<object>(GetProperty<object>(result, "data"), "newValue");
            Assert.That(newValue, Is.Not.Null);
            var vector = GetProperty<object>(newValue, "value");
            Assert.That(Convert.ToSingle(GetProperty<object>(vector, "x")), Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(Convert.ToSingle(GetProperty<object>(vector, "y")), Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(Convert.ToSingle(GetProperty<object>(vector, "z")), Is.EqualTo(3.75f).Within(0.0001f));
        }

        [Test]
        public void SetComponentProperty_BatchTargets_ReturnNewValuePerTarget()
        {
            var first = CreateObject();
            var second = CreateObject();
            var firstBody = first.AddComponent<Rigidbody>();
            var secondBody = second.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperty(
                component: "Rigidbody",
                property: "m_Mass",
                value: "4.25",
                find_method: "by_name",
                targets: first.name + "," + second.name);

            AssertSuccess(result);
            Assert.That(firstBody.mass, Is.EqualTo(4.25f).Within(0.0001f));
            Assert.That(secondBody.mass, Is.EqualTo(4.25f).Within(0.0001f));

            var items = GetProperty<IEnumerable>(GetProperty<object>(result, "data"), "results").Cast<object>().ToList();
            Assert.That(items, Has.Count.EqualTo(2));
            foreach (var item in items)
                AssertReadBackValue(GetProperty<object>(item, "newValue"), 4.25f);
        }

        [Test]
        public void SetComponentProperty_BatchTargets_ReportMissingComponentAsPartialFailure()
        {
            var withBody = CreateObject();
            var withoutBody = CreateObject();
            var body = withBody.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperty(
                component: "Rigidbody",
                property: "m_Mass",
                value: "6",
                find_method: "by_name",
                targets: withBody.name + "," + withoutBody.name);

            AssertSuccess(result);
            Assert.That(body.mass, Is.EqualTo(6f));
            var data = GetProperty<object>(result, "data");
            Assert.That(GetProperty<int>(data, "successCount"), Is.EqualTo(1));
            Assert.That(GetProperty<int>(data, "failCount"), Is.EqualTo(1));

            var items = GetProperty<IEnumerable>(data, "results").Cast<object>().ToList();
            var successItem = items.Single(item => GetProperty<bool>(item, "ok"));
            var failedItem = items.Single(item => !GetProperty<bool>(item, "ok"));
            AssertReadBackValue(GetProperty<object>(successItem, "newValue"), 6f);
            Assert.That(GetProperty<string>(failedItem, "error"), Is.EqualTo("COMPONENT_NOT_ON_TARGET"));
            Assert.That(GetProperty<object>(failedItem, "newValue"), Is.Null);
        }

        [Test]
        public void SetComponentProperties_BatchTargets_ReturnAppliedFieldsPerTarget()
        {
            var first = CreateObject();
            var second = CreateObject();
            var firstBody = first.AddComponent<Rigidbody>();
            var secondBody = second.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperties(
                component: "Rigidbody",
                properties: "{\"m_Mass\":2.75}",
                find_method: "by_name",
                targets: first.name + "," + second.name);

            AssertSuccess(result);
            Assert.That(firstBody.mass, Is.EqualTo(2.75f).Within(0.0001f));
            Assert.That(secondBody.mass, Is.EqualTo(2.75f).Within(0.0001f));

            var items = GetProperty<IEnumerable>(GetProperty<object>(result, "data"), "results").Cast<object>().ToList();
            Assert.That(items, Has.Count.EqualTo(2));
            foreach (var item in items)
            {
                var applied = GetProperty<Dictionary<string, object>>(item, "applied");
                Assert.That(applied.ContainsKey("m_Mass"), Is.True);
                AssertReadBackValue(applied["m_Mass"], 2.75f);
            }
        }

        [Test]
        public void SetComponentProperties_EmptyObject_ReturnsErrorWithoutMutation()
        {
            var go = CreateObject();
            var body = go.AddComponent<Rigidbody>();

            var result = ComponentPropertyFunctions.SetComponentProperties(
                target: go.name,
                component: "Rigidbody",
                properties: "{}",
                find_method: "by_name");

            AssertError(result, "PROPERTIES_REQUIRED");
            Assert.That(body.mass, Is.EqualTo(1f));
        }

        [Test]
        public void ResolveMany_BoundedOverload_AllowsLimitAndSignalsOverflow()
        {
            var first = CreateObject();
            var second = CreateObject();
            var third = CreateObject();

            var atLimit = ObjectsHelper.ResolveMany(
                first.name + "," + second.name,
                "by_name",
                searchInactive: true,
                maxResults: 2,
                limitExceeded: out var atLimitExceeded);
            var overLimit = ObjectsHelper.ResolveMany(
                first.name + "," + second.name + "," + third.name,
                "by_name",
                searchInactive: true,
                maxResults: 2,
                limitExceeded: out var overLimitExceeded);

            Assert.That(atLimitExceeded, Is.False);
            Assert.That(atLimit, Has.Count.EqualTo(2));
            Assert.That(overLimitExceeded, Is.True);
            Assert.That(overLimit, Has.Count.EqualTo(3));
        }

        [Test]
        public void BatchMutators_MoreThanLimit_RejectWithoutMutation()
        {
            var objects = Enumerable.Range(0, ObjectsHelper.DefaultBatchTargetLimit + 1)
                .Select(_ => CreateObject())
                .ToList();
            var targets = string.Join(",", objects.Select(go => go.name));

            var transformResult = GameObjectFunctions.SetTransform(
                position: "8,0,0",
                find_method: "by_name",
                targets: targets);
            var activeResult = GameObjectFunctions.SetActive(
                active: "false",
                find_method: "by_name",
                targets: targets);
            var propertyResult = ComponentPropertyFunctions.SetComponentProperty(
                component: "Transform",
                property: "position",
                value: "{\"x\":8,\"y\":0,\"z\":0}",
                find_method: "by_name",
                targets: targets);
            var addResult = ComponentBatchFunctions.AddComponentToMany(
                targets,
                "BoxCollider",
                "by_name");

            AssertError(transformResult, "TOO_MANY_TARGETS");
            AssertError(activeResult, "TOO_MANY_TARGETS");
            AssertError(propertyResult, "TOO_MANY_TARGETS");
            AssertError(addResult, "TOO_MANY_TARGETS");
            Assert.That(objects.All(go => go.transform.position == Vector3.zero), Is.True);
            Assert.That(objects.All(go => go.activeSelf), Is.True);
            Assert.That(objects.All(go => go.GetComponent<BoxCollider>() == null), Is.True);
        }

        private GameObject CreateObject()
        {
            var go = new GameObject("MultiTargetEditing_" + Guid.NewGuid().ToString("N"));
            _objects.Add(go);
            return go;
        }

        private static void AssertSuccess(object result)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(GetProperty<bool>(result, "success"), Is.True);
        }

        private static void AssertError(object result, string code)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(GetProperty<bool>(result, "success"), Is.False);
            Assert.That(GetProperty<string>(result, "code"), Is.EqualTo(code));
        }

        private static void AssertReadBackValue(object readBack, float expected)
        {
            Assert.That(readBack, Is.Not.Null);
            Assert.That(Convert.ToSingle(GetProperty<object>(readBack, "value")),
                Is.EqualTo(expected).Within(0.0001f));
        }

        private static T GetProperty<T>(object target, string name)
        {
            Assert.That(target, Is.Not.Null);
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, $"Missing property '{name}' on {target.GetType().Name}.");
            return (T)property.GetValue(target);
        }

        private static void RestoreSceneDirtiness(Scene scene, bool wasDirty)
        {
            if (wasDirty || !scene.IsValid())
                return;

            var method = typeof(EditorSceneManager).GetMethod(
                "ClearSceneDirtiness",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
                method.Invoke(null, new object[] { scene });
        }
    }
}
