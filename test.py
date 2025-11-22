import random

def quicksort(arr):
    if len(arr) <= 1:
        return arr
    pivot = arr[len(arr) // 2]
    left = [x for x in arr if x < pivot]
    middle = [x for x in arr if x == pivot]
    right = [x for x in arr if x > pivot]
    return quicksort(left) + middle + quicksort(right)

if __name__ == '__main__':
    array = random.sample(range(1, 100), 20)
    print('Original Array:', array)
    sorted_array = quicksort(array)
    print('Sorted Array:', sorted_array)